using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Db;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Servicio de categorías: compone el CRUD genérico y delega en él las cinco
/// operaciones. Las reglas viven en <see cref="CategoryRules"/>.
/// </summary>
public class CategoryService : ICategoryService
{
  private readonly ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto> _crud;
  private readonly ICategoryRepository _repository;
  private readonly IEntityMapper<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto> _mapper;
  private readonly ITransactionRunner _transactions;

  public CategoryService(
      ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto> crud,
      ICategoryRepository repository,
      IEntityMapper<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto> mapper,
      ITransactionRunner transactions)
  {
    _transactions = transactions;
    _crud = crud;
    _repository = repository;
    _mapper = mapper;
  }

  // ---- CRUD delegado ------------------------------------------------------

  public Task<IEnumerable<CategoryDto>> GetAllAsync(CancellationToken ct = default)
      => _crud.GetAllAsync(ct);

  public Task<PagedResult<CategoryDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default)
      => _crud.GetPagedAsync(query, ct);

  public Task<CategoryDto> GetByIdAsync(int id, CancellationToken ct = default)
      => _crud.GetByIdAsync(id, ct);

  public Task<int> CreateAsync(CreateCategoryDto dto, CancellationToken ct = default)
      => _crud.CreateAsync(dto, ct);

  public Task UpdateAsync(int id, UpdateCategoryDto dto, CancellationToken ct = default)
      => _crud.UpdateAsync(id, dto, ct);

  public Task DeleteAsync(int id, CancellationToken ct = default)
      => _crud.DeleteAsync(id, ct);

  // ---- operaciones propias de Category ------------------------------------

  public async Task<CategoryDto> GetBySlugAsync(string slug, CancellationToken ct = default)
      => _mapper.ToDto(await _repository.GetBySlugAsync(slug, ct)
          ?? throw new NotFoundAppException("Category", slug));

  public async Task<IReadOnlyList<CategoryDto>> GetFeaturedAsync(CancellationToken ct = default)
      => [.. (await _repository.GetFeaturedAsync(ct)).Select(_mapper.ToDto)];

  public async Task<IReadOnlyList<CategoryDto>> SetFeaturedAsync(
      IReadOnlyList<int> orderedIds, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(orderedIds);

    // El mensaje bonito. La garantía es el CHECK de la base: aunque esto faltara, una cuarta
    // no cabe.
    if (orderedIds.Count > MaxFeatured)
      throw CatalogErrors.FeaturedLimitReached(MaxFeatured);

    if (orderedIds.Distinct().Count() != orderedIds.Count)
      throw new BadOperationAppException("A category can't be featured twice.");

    if (await _repository.CountExistingAsync(orderedIds, ct) != orderedIds.Count)
      throw new BadOperationAppException("Some of the categories to feature do not exist.");

    await _transactions.ExecuteAsync(async token =>
    {
      await _repository.ReplaceFeaturedAsync(orderedIds, token);
      return true;
    }, ct);

    return await GetFeaturedAsync(ct);
  }

  /// <summary>Tope de destacadas. Tiene que casar con el CHECK de <c>AppDbContext</c>.</summary>
  public const int MaxFeatured = 3;
}




/* VERSIÓN ANTERIOR — se conserva como registro de aprendizaje.
   CRUD copiado por entidad, con excepciones BCL como señal de negocio que el
   controller traducía a HTTP con try/catch.

public class CategoryService : ICategoryService
{
  private readonly ICategoryRepository _repository;
  private readonly IMapper _mapper;

  public CategoryService(ICategoryRepository repository, IMapper mapper)
  {
    _repository = repository;
    _mapper = mapper;
  }

  public async Task<IEnumerable<CategoryDto>> GetAllAsync()
  {
    var entities = await _repository.GetAllAsync();
    return _mapper.Map<IEnumerable<CategoryDto>>(entities);
  }

  public async Task<CategoryDto?> GetByIdAsync(int id)
  {
    var entity = await _repository.GetByIdAsync(id);
    return entity is null ? null : _mapper.Map<CategoryDto>(entity);
  }

  public async Task<int> CreateAsync(CreateCategoryDto dto)
  {
    if (await _repository.NameExistsAsync(dto.Name))
    {
      throw new InvalidOperationException("Category already exists.");
    }

    var entity = _mapper.Map<Category>(dto);
    await _repository.AddAsync(entity);
    return entity.Id;
  }

  public async Task UpdateAsync(int id, CreateCategoryDto dto)
  {
    var existing = await _repository.GetByIdAsync(id);
    if (existing is null)
    {
      throw new KeyNotFoundException($"Category with id {id} not found.");
    }

    _mapper.Map(dto, existing); // map into existing entity
    await _repository.UpdateAsync(existing);
  }

  public async Task DeleteAsync(int id)
  {
    if (!await _repository.ExistsAsync(id))
    {
      throw new KeyNotFoundException($"Category with id {id} not found.");
    }

    await _repository.DeleteAsync(id);
  }
}
*/
