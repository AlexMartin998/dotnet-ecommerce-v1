using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Servicio de categorías. <b>Compone</b> el CRUD genérico en lugar de heredarlo:
/// las cinco operaciones se delegan tal cual y las reglas viven en
/// <see cref="CategoryRules"/>.
/// </summary>
/// <remarks>
/// Los cinco reenvíos son el precio explícito de la composición. A cambio, esta clase
/// no tiene estado heredado que pueda romperse, y el día que Category necesite algo
/// propio (p. ej. <c>GetWithProductCountAsync</c>) se agrega aquí sin tocar el CRUD.
/// </remarks>
public class CategoryService : ICategoryService
{
  private readonly ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto> _crud;

  public CategoryService(ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto> crud)
  {
    _crud = crud;
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
  // (por ahora ninguna)
}




/* VERSIÓN ANTERIOR — se conserva como registro de aprendizaje.
   Escrita a mano, sin reutilizar nada: cada entidad nueva copiaba y pegaba estos
   cinco métodos. Además lanzaba excepciones BCL (InvalidOperationException /
   KeyNotFoundException) como señal de negocio, que el controller tenía que
   traducir a HTTP con try/catch. Hoy: CrudService + CategoryRules + handler global.

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
