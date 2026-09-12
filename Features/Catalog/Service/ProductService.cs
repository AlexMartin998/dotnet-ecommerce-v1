using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Features.Catalog.Events;
using ApiEcommerce.Shared.Storage;
using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Mapping;
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
  private readonly IIdempotentCommandRunner _runner;
  private readonly IEntityMapper<Product, ProductDto, CreateProductDto, UpdateProductDto> _mapper;

  public ProductService(
      ICrudService<ProductDto, CreateProductDto, UpdateProductDto> crud,
      IProductRepository repository,
      ICategoryRepository categoryRepository,
      IFileStorage storage,
      IEventOutbox outbox,
      IIdempotentCommandRunner runner,
      IEntityMapper<Product, ProductDto, CreateProductDto, UpdateProductDto> mapper)
  {
    _runner = runner;
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
      => [.. (await _repository.GetAllWithCategoryAsync(ct)).Select(_mapper.ToDto)];

  public async Task<PagedResult<ProductDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    var page = await _repository.GetPagedWithCategoryAsync(query.Page, query.PageSize, ct);

    return new PagedResult<ProductDto>(
        [.. page.Items.Select(_mapper.ToDto)],
        page.Page, page.PageSize, page.TotalItems);
  }

  public async Task<ProductDto> GetByIdAsync(int id, CancellationToken ct = default)
  {
    var product = await _repository.GetByIdWithCategoryAsync(id, ct)
        ?? throw new NotFoundAppException("Product", id);

    return _mapper.ToDto(product);
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
    return [.. products.Select(_mapper.ToDto)];
  }

  public async Task<IEnumerable<ProductDto>> SearchAsync(string name, CancellationToken ct = default)
  {
    var products = await _repository.SearchProductAsync(name, ct);
    return [.. products.Select(_mapper.ToDto)];
  }

  public async Task<ProductDto> SetImageAsync(int id, FileUpload upload, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(upload);

    var product = await _repository.GetByIdAsync(id, ct)
        ?? throw new NotFoundAppException("Product", id);

    var previous = product.ImageUrl;

    // Se guarda la nueva antes de borrar la vieja: si la validación falla, el producto
    // conserva la imagen que ya tenía.
    var stored = await _storage.SaveImageAsync(upload, ct);

    product.ImageUrl = stored;
    await _repository.UpdateAsync(product, ct);

    await _storage.DeleteAsync(previous, ct);

    return _mapper.ToDto(await _repository.GetByIdWithCategoryAsync(id, ct) ?? product);
  }

  public Task<CommandOutcome<ProductDto>> BuyAsync(
      BuyProductDto dto, CommandIntent intent, string? buyerUserId = null,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    // Descontar stock, emitir el evento y recordar el intento son atómicos entre sí. La
    // envoltura la pone el runner; la lambda ha de ser replayable, o sea releer lo que use.
    return _runner.RunAsync(intent, dto, async token =>
    {
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

      return _mapper.ToDto(updated);
    }, ct);
  }
}
