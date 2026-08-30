using ApiEcommerce.Exceptions;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Repository;
using ApiEcommerce.Service.Crud;
using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Storage;
using AutoMapper;

namespace ApiEcommerce.Service;


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
  private readonly IMapper _mapper;

  public ProductService(
      ICrudService<ProductDto, CreateProductDto, UpdateProductDto> crud,
      IProductRepository repository,
      ICategoryRepository categoryRepository,
      IFileStorage storage,
      IMapper mapper)
  {
    _crud = crud;
    _repository = repository;
    _categoryRepository = categoryRepository;
    _storage = storage;
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

  public async Task<ProductDto> BuyAsync(BuyProductDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    var product = await _repository.GetBySkuAsync(dto.SKU, ct)
        ?? throw new NotFoundAppException("Product", dto.SKU);

    // 409 y no 400: el request es válido en sí mismo, choca con el estado de la base.
    if (product.Stock < dto.Quantity)
      throw new ConflictAppException(
          $"Insufficient stock for SKU '{product.SKU}': available {product.Stock}, requested {dto.Quantity}.");

    product.Stock -= dto.Quantity;
    // UpdatedAt lo estampa AppDbContext (ver IAuditable)
    await _repository.UpdateAsync(product, ct);

    return _mapper.Map<ProductDto>(product);
  }
}
