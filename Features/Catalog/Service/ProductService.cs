using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Db;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Features.Catalog.Events;
using ApiEcommerce.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using AutoMapper;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Shared.Idempotency;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Servicio de productos. Compone tres colaboradores: el CRUD genérico, el
/// repositorio propio (para las consultas de dominio) y el mapper.
/// </summary>
/// <remarks>
/// Este es justo el caso que la herencia hacía incómodo: <c>ProductService</c>
/// necesita el CRUD <b>y</b> consultas propias <b>y</b> el mapper. Con composición
/// cada colaborador entra por el constructor y se ve de un vistazo qué usa.
/// </remarks>
public class ProductService : IProductService
{
  private readonly ICrudService<ProductDto, CreateProductDto, UpdateProductDto> _crud;
  private readonly IProductRepository _repository;
  private readonly ICategoryRepository _categoryRepository;
  private readonly IFileStorage _storage;
  private readonly IEventOutbox _outbox;
  private readonly ITransactionRunner _tx;
  private readonly ICommandLog _commands;
  private readonly IMapper _mapper;

  public ProductService(
      ICrudService<ProductDto, CreateProductDto, UpdateProductDto> crud,
      IProductRepository repository,
      ICategoryRepository categoryRepository,
      IFileStorage storage,
      IEventOutbox outbox,
      ITransactionRunner tx,
      ICommandLog commands,
      IMapper mapper)
  {
    _tx = tx;
    _commands = commands;
    _crud = crud;
    _repository = repository;
    _categoryRepository = categoryRepository;
    _storage = storage;
    _outbox = outbox;
    _mapper = mapper;
  }

  // ---- CRUD: escrituras delegadas, lecturas propias -----------------------

  // GetAll y GetById NO se delegan: el CRUD genérico no carga la navegación
  // Category y ProductDto.CategoryName saldría siempre nulo. Poder sustituir dos
  // de las cinco operaciones sin tocar el componente CRUD es justo lo que compra
  // la composición.

  public async Task<IEnumerable<ProductDto>> GetAllAsync(CancellationToken ct = default)
      => _mapper.Map<IEnumerable<ProductDto>>(await _repository.GetAllWithCategoryAsync(ct));

  public async Task<PagedResult<ProductDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    // Igual que GetAll: se usa el método del repositorio que hace Include(Category),
    // no el genérico, o CategoryName saldría vacío.
    var page = await _repository.GetPagedWithCategoryAsync(query.Page, query.PageSize, ct);

    return new PagedResult<ProductDto>(
        [.. _mapper.Map<IEnumerable<ProductDto>>(page.Items)],
        page.Page, page.PageSize, page.TotalItems);
  }

  public async Task<ProductDto> GetByIdAsync(int id, CancellationToken ct = default)
  {
    var product = await _repository.GetByIdWithCategoryAsync(id, ct)
        ?? throw new NotFoundAppException("Product", id);

    return _mapper.Map<ProductDto>(product);
  }

  public Task<int> CreateAsync(CreateProductDto dto, CancellationToken ct = default)
      => _crud.CreateAsync(dto, ct);

  public Task UpdateAsync(int id, UpdateProductDto dto, CancellationToken ct = default)
      => _crud.UpdateAsync(id, dto, ct);

  // Delete NO se delega tal cual: además de borrar la fila hay que borrar el archivo,
  // o cada producto eliminado deja su imagen huérfana en disco para siempre.
  public async Task DeleteAsync(int id, CancellationToken ct = default)
  {
    var product = await _repository.GetByIdAsync(id, ct)
        ?? throw new NotFoundAppException("Product", id);

    var imagePath = product.ImageUrl;

    await _crud.DeleteAsync(id, ct);

    // Después del borrado en base: si la fila no se pudo borrar (409 por FK, por
    // ejemplo), el archivo debe seguir existiendo.
    await _storage.DeleteAsync(imagePath, ct);
  }

  // ---- operaciones propias de Product --------------------------------------

  public async Task<IEnumerable<ProductDto>> GetForCategoryAsync(int categoryId, CancellationToken ct = default)
  {
    // Aquí sí es 404: la categoría es el recurso pedido en la ruta.
    if (!await _categoryRepository.ExistsAsync(categoryId, ct))
      throw new NotFoundAppException("Category", categoryId);

    var products = await _repository.GetProductsForCategoryAsync(categoryId, ct);
    return _mapper.Map<IEnumerable<ProductDto>>(products);
  }

  public async Task<IEnumerable<ProductDto>> SearchAsync(string name, CancellationToken ct = default)
  {
    var products = await _repository.SearchProductAsync(name, ct);
    return _mapper.Map<IEnumerable<ProductDto>>(products);
  }

  public async Task<ProductDto> SetImageAsync(int id, FileUpload upload, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(upload);

    var product = await _repository.GetByIdAsync(id, ct)
        ?? throw new NotFoundAppException("Product", id);

    var previous = product.ImageUrl;

    // Se guarda la nueva ANTES de borrar la vieja: si la validación falla, el
    // producto conserva la imagen que ya tenía.
    var stored = await _storage.SaveProductImageAsync(upload, ct);

    product.ImageUrl = stored;
    await _repository.UpdateAsync(product, ct);

    await _storage.DeleteAsync(previous, ct);

    return _mapper.Map<ProductDto>(await _repository.GetByIdWithCategoryAsync(id, ct) ?? product);
  }

  public async Task<CommandOutcome<ProductDto>> BuyAsync(
      BuyProductDto dto, CommandIntent intent, string? buyerUserId = null,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    try
    {
      // La atomicidad "descontar stock + emitir el evento + dejar constancia de que este
      // intento ya se ejecutó" es una regla de NEGOCIO, así que la transacción vive aquí
      // y no en un atributo del controller. Antes dependía de que alguien no olvidara
      // poner [Transactional] en la acción: llamar a BuyAsync desde un job o desde otro
      // endpoint descontaba stock sin emitir el evento, en silencio y sin error.
      //
      // La lambda es REPLAYABLE (relee todo lo que necesita), que es lo que exige
      // ITransactionRunner para poder reintentar ante un fallo transitorio.
      return await _tx.ExecuteAsync(async token =>
      {
        // ⚠️ Esta comprobación va DENTRO de la transacción, y ahí está toda la diferencia
        // con la versión que vivía en Redis. La marca y el efecto se confirman juntos o
        // no se confirman: no existe la ventana en la que la compra ocurrió y nadie la
        // recuerda (proceso muerto entre el commit y el guardado), ni la de "el almacén
        // no contestó, ejecuto sin garantía". Si la base no está, tampoco hay compra.
        if (await _commands.FindResultAsync<ProductDto>(intent, dto, token) is { } already)
          return new CommandOutcome<ProductDto>(already, WasReplayed: true);

        var product = await _repository.GetBySkuAsync(dto.SKU, token)
            ?? throw new NotFoundAppException("Product", dto.SKU);

        // El descuento y la comprobación de stock ocurren en la MISMA sentencia SQL.
        // Comprobar aquí `product.Stock < dto.Quantity` y descontar después sería
        // read-then-write: entre las dos cosas cabe otra compra y se vende dos veces
        // la última unidad.
        if (!await _repository.TryDecrementStockAsync(product.Id, dto.Quantity, token))
          throw new ConflictAppException(
              $"Insufficient stock for SKU '{product.SKU}': requested {dto.Quantity}.");

        // Relectura para devolver el estado real: ExecuteUpdate no toca el change
        // tracker, así que la instancia que ya teníamos sigue con el stock anterior.
        var updated = await _repository.GetByIdWithCategoryAsync(product.Id, token) ?? product;

        // El evento se ESCRIBE aquí y se PUBLICA después (OutboxPublisher). Publicar
        // directo a RabbitMQ en esta línea ataría la compra a que el broker esté vivo,
        // y dejaría anunciada una compra que todavía podría no confirmarse.
        await _outbox.EnqueueAsync(new ProductPurchased(
            updated.Id, updated.SKU, updated.Name, dto.Quantity, updated.Stock,
            updated.Price, buyerUserId, DateTime.Now), token);

        var result = _mapper.Map<ProductDto>(updated);

        // Deja constancia del intento. Tampoco hace SaveChanges: lo confirma el
        // SaveChangesAsync de abajo, junto al evento y al descuento de stock.
        _commands.Record(intent, dto, result);

        // Confirma la fila del outbox y la del comando dentro de la transacción que
        // abrió el runner. El choque de clave primaria de ExecutedCommands sale AQUÍ.
        await _repository.SaveChangesAsync(token);

        return new CommandOutcome<ProductDto>(result, WasReplayed: false);
      }, ct);
    }
    catch (Exception ex) when (_commands.IsDuplicateIntent(ex))
    {
      // Otra réplica ejecutó el MISMO intento a la vez y confirmó primero. Nuestra
      // transacción entera se deshizo —incluido el descuento de stock—, así que no hay
      // nada que compensar: basta con devolver lo que hizo el ganador.
      //
      // ⚠️ Esto no es una carrera que haya que evitar, es la carrera resolviéndose. La
      // base bloquea a la segunda inserción en la clave hasta que la primera confirma;
      // por eso no hacen falta ni reserva, ni TTL, ni un estado "en curso".
      var winner = await _commands.FindResultAsync<ProductDto>(intent, dto, ct)
          ?? throw new ConflictAppException(
              "A concurrent request with the same idempotency key is still in progress.");

      return new CommandOutcome<ProductDto>(winner, WasReplayed: true);
    }
  }
}
