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
/// Servicio de productos: compone el CRUD genérico con el repositorio propio para
/// las consultas y operaciones de dominio.
/// </summary>
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

  // Las lecturas no se delegan: el CRUD genérico no carga la navegación Category y
  // ProductDto.CategoryName saldría siempre nulo.

  public async Task<IEnumerable<ProductDto>> GetAllAsync(CancellationToken ct = default)
      => _mapper.Map<IEnumerable<ProductDto>>(await _repository.GetAllWithCategoryAsync(ct));

  public async Task<PagedResult<ProductDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

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

  // Delete no se delega tal cual: además de la fila hay que borrar la imagen del disco.
  public async Task DeleteAsync(int id, CancellationToken ct = default)
  {
    var product = await _repository.GetByIdAsync(id, ct)
        ?? throw new NotFoundAppException("Product", id);

    var imagePath = product.ImageUrl;

    await _crud.DeleteAsync(id, ct);

    // Después del borrado en base: si la fila no se pudo borrar, el archivo debe seguir.
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

    // Se guarda la nueva antes de borrar la vieja: si la validación falla, el producto
    // conserva la imagen que ya tenía.
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
      // Descontar stock, emitir el evento y registrar el intento son atómicos entre sí:
      // la transacción vive aquí, y no en un atributo del controller, porque es la regla.
      // La lambda es replayable (relee todo lo que necesita), como exige ITransactionRunner.
      return await _tx.ExecuteAsync(async token =>
      {
        // Dentro de la transacción: la marca y el efecto se confirman juntos o no se
        // confirman, así que no hay ventana en la que la compra ocurra y nadie la recuerde.
        if (await _commands.FindResultAsync<ProductDto>(intent, dto, token) is { } already)
          return new CommandOutcome<ProductDto>(already, WasReplayed: true);

        var product = await _repository.GetBySkuAsync(dto.SKU, token)
            ?? throw new NotFoundAppException("Product", dto.SKU);

        // Comprobación y descuento van en la misma sentencia SQL: comprobar el stock aquí
        // y descontar después sería read-then-write y vendería dos veces la última unidad.
        if (!await _repository.TryDecrementStockAsync(product.Id, dto.Quantity, token))
          throw new ConflictAppException(
              $"Insufficient stock for SKU '{product.SKU}': requested {dto.Quantity}.");

        // Relectura: ExecuteUpdate no toca el change tracker y la instancia que ya
        // teníamos sigue con el stock anterior.
        var updated = await _repository.GetByIdWithCategoryAsync(product.Id, token) ?? product;

        // El evento se escribe aquí y lo publica después OutboxPublisher: publicar a
        // RabbitMQ en esta línea ataría la compra a que el broker esté vivo.
        await _outbox.EnqueueAsync(new ProductPurchased(
            updated.Id, updated.SKU, updated.Name, dto.Quantity, updated.Stock,
            updated.Price, buyerUserId, DateTime.Now), token);

        var result = _mapper.Map<ProductDto>(updated);

        // No hace SaveChanges: lo confirma el de abajo, junto al evento y al descuento.
        _commands.Record(intent, dto, result);

        // El choque de clave primaria de ExecutedCommands sale aquí.
        await _repository.SaveChangesAsync(token);

        return new CommandOutcome<ProductDto>(result, WasReplayed: false);
      }, ct);
    }
    catch (Exception ex) when (_commands.IsDuplicateIntent(ex))
    {
      // Otra réplica ejecutó el mismo intento y confirmó primero: nuestra transacción
      // entera se deshizo, así que no hay nada que compensar y basta devolver su
      // resultado. La base serializa la carrera en la clave; no hace falta ni TTL ni reserva.
      var winner = await _commands.FindResultAsync<ProductDto>(intent, dto, ct)
          ?? throw new ConflictAppException(
              "A concurrent request with the same idempotency key is still in progress.");

      return new CommandOutcome<ProductDto>(winner, WasReplayed: true);
    }
  }
}
