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
using ApiEcommerce.Shared.Db;

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
  private readonly IProductVariantRepository _variants;
  private readonly ITransactionRunner _transactions;
  private readonly IEntityMapper<Product, ProductDto, CreateProductDto, UpdateProductDto> _mapper;

  public ProductService(
      ICrudService<ProductDto, CreateProductDto, UpdateProductDto> crud,
      IProductRepository repository,
      ICategoryRepository categoryRepository,
      IFileStorage storage,
      IEventOutbox outbox,
      IIdempotentCommandRunner runner,
      IEntityMapper<Product, ProductDto, CreateProductDto, UpdateProductDto> mapper,
      IProductVariantRepository variants,
      ITransactionRunner transactions)
  {
    _variants = variants;
    _transactions = transactions;
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

  /// <summary>Hasta dónde se considera stock bajo.</summary>
  /// <remarks>
  /// Constante y no configuración: es un criterio de presentación del panel, no una regla de
  /// negocio, y hacerlo configurable añade una opción que validar sin que nadie la cambie.
  /// </remarks>
  private const int LowStockThreshold = 10;

  public async Task<ProductStatsDto> GetStatsAsync(CancellationToken ct = default)
  {
    var (total, outOfStock, lowStock) = await _repository.CountStockAsync(LowStockThreshold, ct);

    return new ProductStatsDto
    {
      Total = total,
      OutOfStock = outOfStock,
      LowStock = lowStock,
      LowStockThreshold = LowStockThreshold
    };
  }

  public Task<int> CreateAsync(CreateProductDto dto, CancellationToken ct = default)
      => _crud.CreateAsync(dto, ct);

  public Task UpdateAsync(int id, UpdateProductDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    if (string.IsNullOrWhiteSpace(dto.SKU))
      return _crud.UpdateAsync(id, dto, ct);

    // Renombrar el SKU de un producto sin tallas renombra el de su variante, que es el que
    // manda el carrito: separarlos dejaba una ficha con un SKU que no se podía comprar. En
    // un producto con tallas no hay variante sin talla y la sentencia no toca nada.
    return _transactions.ExecuteAsync(async token =>
    {
      await _crud.UpdateAsync(id, dto, token);
      await _variants.SyncUnsizedSkuAsync(id, dto.SKU, token);
      return true;
    }, ct);
  }

  // Delete no se delega: retirar un producto es marcarlo, no borrar la fila. Las líneas de
  // orden lo referencian con clave foránea, y un comprobante de ayer tiene que seguir
  // apuntando a algo. Las imágenes se quedan: el producto puede volver.
  public async Task DeleteAsync(int id, CancellationToken ct = default)
  {
    // Transacción: retirar el producto y liberar los SKU de sus variantes son dos UPDATE, y
    // a medias quedaría un producto invisible cuyo SKU no se puede reutilizar.
    var deleted = await _transactions.ExecuteAsync(
        token => _repository.SoftDeleteAsync(id, token), ct);

    if (!deleted)
      throw new NotFoundAppException("Product", id);
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

  public async Task<IEnumerable<ProductDto>> GetForCategorySlugAsync(
      string categorySlug, CancellationToken ct = default)
  {
    var category = await _categoryRepository.GetBySlugAsync(categorySlug, ct)
        ?? throw new NotFoundAppException("Category", categorySlug);

    var products = await _repository.GetProductsForCategoryAsync(category.Id, ct);
    return [.. products.Select(_mapper.ToDto)];
  }

  public async Task<IEnumerable<ProductDto>> SearchAsync(string name, CancellationToken ct = default)
  {
    var products = await _repository.SearchProductAsync(name, ct);
    return [.. products.Select(_mapper.ToDto)];
  }

  /// <summary>Tope de imágenes por producto.</summary>
  /// <remarks>
  /// Sin tope, subir imágenes es escritura ilimitada en disco por parte de cualquier
  /// administrador, y la respuesta del catálogo crece sin control.
  /// </remarks>
  private const int MaxImages = 8;

  public async Task<ProductDto> AddImageAsync(int id, FileUpload upload, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(upload);

    var product = await _repository.GetByIdWithImagesAsync(id, ct)
        ?? throw new NotFoundAppException("Product", id);

    if (product.Images.Count >= MaxImages)
      throw new ConflictAppException($"A product can't have more than {MaxImages} images.");

    // El fichero primero: si la validación de magic bytes falla, no hay fila que deshacer.
    var stored = await _storage.SaveImageAsync(upload, ct);

    product.Images.Add(new ProductImage
    {
      Url = stored,
      // Al final de la cola: la 0 sigue siendo la del listado hasta que alguien la quite.
      Position = product.Images.Count == 0 ? 0 : product.Images.Max(i => i.Position) + 1
    });

    await _repository.UpdateAsync(product, ct);

    return _mapper.ToDto(await _repository.GetByIdWithCategoryAsync(id, ct) ?? product);
  }

  public async Task<ProductDto> RemoveImageAsync(int id, int imageId, CancellationToken ct = default)
  {
    var product = await _repository.GetByIdWithImagesAsync(id, ct)
        ?? throw new NotFoundAppException("Product", id);

    var image = product.Images.FirstOrDefault(i => i.Id == imageId)
        ?? throw new NotFoundAppException("ProductImage", imageId);

    product.Images.Remove(image);
    await _repository.UpdateAsync(product, ct);

    // Después del commit: si la fila no se pudo borrar, el fichero debe seguir ahí.
    await _storage.DeleteAsync(image.Url, ct);

    return _mapper.ToDto(await _repository.GetByIdWithCategoryAsync(id, ct) ?? product);
  }

  public async Task<ProductDto> GetBySlugAsync(string slug, CancellationToken ct = default)
      => _mapper.ToDto(await _repository.GetBySlugAsync(slug, ct)
          ?? throw new NotFoundAppException("Product", slug));

  public Task<CommandOutcome<ProductDto>> BuyAsync(
      BuyProductDto dto, CommandIntent intent, string? buyerUserId = null,
      CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    // Descontar stock, emitir el evento y recordar el intento son atómicos entre sí. La
    // envoltura la pone el runner; la lambda ha de ser replayable, o sea releer lo que use.
    return _runner.RunAsync(intent, dto, async token =>
    {
      // El SKU es el de la talla (planning/27); un producto sin tallas tiene la suya con el
      // SKU del producto, así que esta ruta sigue valiendo para él.
      var variant = await _variants.GetBySkuAsync(dto.SKU, token)
          ?? throw new NotFoundAppException("Product", dto.SKU);

      if (!variant.IsActive)
        throw CatalogErrors.SkuUnavailable(variant.SKU);

      // Comprobación y descuento van en la misma sentencia SQL: comprobar el stock aquí
      // y descontar después sería read-then-write y vendería dos veces la última unidad.
      if (!await _variants.TryDecrementStockAsync(variant.Id, dto.Quantity, token))
        throw CatalogErrors.InsufficientStock(variant.SKU, dto.Quantity);

      // Relectura: ExecuteUpdate no toca el change tracker y la instancia que ya
      // teníamos sigue con el stock anterior.
      var updated = await _repository.GetByIdWithCategoryAsync(variant.ProductId, token)
          ?? throw new NotFoundAppException("Product", dto.SKU);

      var remaining = updated.Variants.FirstOrDefault(v => v.Id == variant.Id)?.Stock ?? 0;

      // El evento se escribe aquí y lo publica después OutboxPublisher: publicar a
      // RabbitMQ en esta línea ataría la compra a que el broker esté vivo.
      await _outbox.EnqueueAsync(new ProductPurchased(
          updated.Id, variant.SKU, updated.Name, dto.Quantity, remaining,
          updated.Price, buyerUserId, DateTime.Now, variant.Size), token);

      // // Hasta planning/27 el stock era del producto.
      // var product = await _repository.GetBySkuAsync(dto.SKU, token)
      //     ?? throw new NotFoundAppException("Product", dto.SKU);
      //
      // // Comprobación y descuento van en la misma sentencia SQL: comprobar el stock aquí
      // // y descontar después sería read-then-write y vendería dos veces la última unidad.
      // if (!await _repository.TryDecrementStockAsync(product.Id, dto.Quantity, token))
      //   throw new ConflictAppException(
      //       $"Insufficient stock for SKU '{product.SKU}': requested {dto.Quantity}.");
      //
      // // Relectura: ExecuteUpdate no toca el change tracker y la instancia que ya
      // // teníamos sigue con el stock anterior.
      // var updated = await _repository.GetByIdWithCategoryAsync(product.Id, token) ?? product;
      //
      // // El evento se escribe aquí y lo publica después OutboxPublisher: publicar a
      // // RabbitMQ en esta línea ataría la compra a que el broker esté vivo.
      // await _outbox.EnqueueAsync(new ProductPurchased(
      //     updated.Id, updated.SKU, updated.Name, dto.Quantity, updated.Stock,
      //     updated.Price, buyerUserId, DateTime.Now), token);

      return _mapper.ToDto(updated);
    }, ct);
  }
}
