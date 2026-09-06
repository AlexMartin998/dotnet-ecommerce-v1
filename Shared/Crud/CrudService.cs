using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Paging;
using AutoMapper;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Shared.Crud;


/// <summary>
/// Implementación única y reutilizable del CRUD de la capa de servicio: mapea DTO ↔
/// entidad, delega la persistencia en <see cref="IBaseRepository{T}"/> y consulta las
/// reglas de negocio de la entidad en <see cref="IEntityRules{TEntity, TCreateDto, TUpdateDto}"/>.
/// </summary>
/// <remarks>
/// <para>
/// <b>Composición, no herencia.</b> Los servicios por entidad (<c>CategoryService</c>,
/// <c>ProductService</c>) <i>tienen</i> un <c>ICrudService</c> y le delegan; no heredan
/// de él. Por eso la clase es <c>sealed</c>: sus invariantes —"antes de escribir se
/// evalúan las reglas", "el update mapea sobre la entidad rastreada"— no se pueden
/// sobreescribir por accidente desde una subclase.
/// </para>
/// <para>
/// Lo que se gana frente a una clase base abstracta con hooks <c>virtual</c>:
/// reglas testeables por separado, un servicio de entidad libre de componer varios
/// colaboradores, y cero reflexión (el <c>Id</c> lo garantiza <see cref="IEntity"/>).
/// Lo que se paga: cinco métodos de delegación por entidad. Es un precio explícito y
/// aceptado; ver <c>AGENTS/docs/03-service.md</c>.
/// </para>
/// </remarks>
public sealed class CrudService<TEntity, TDto, TCreateDto, TUpdateDto>(
    IBaseRepository<TEntity> repository,
    IMapper mapper,
    IEntityRules<TEntity, TCreateDto, TUpdateDto> rules)
  : ICrudService<TDto, TCreateDto, TUpdateDto>
  where TEntity : class, IEntity
{

  public async Task<IEnumerable<TDto>> GetAllAsync(CancellationToken ct = default)
      => mapper.Map<IEnumerable<TDto>>(await repository.GetAllAsync(ct));

  public async Task<PagedResult<TDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    var page = await repository.GetPagedAsync(query.Page, query.PageSize, ct);

    return new PagedResult<TDto>(
        [.. mapper.Map<IEnumerable<TDto>>(page.Items)],
        page.Page, page.PageSize, page.TotalItems);
  }

  public async Task<TDto> GetByIdAsync(int id, CancellationToken ct = default)
      => mapper.Map<TDto>(await GetOrThrowAsync(id, ct));

  public async Task<int> CreateAsync(TCreateDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    await rules.EnsureCanCreateAsync(dto, ct);

    var entity = mapper.Map<TEntity>(dto);
    await repository.AddAsync(entity, ct);

    return entity.Id; // sin reflexión: IEntity garantiza la propiedad
  }

  public async Task UpdateAsync(int id, TUpdateDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    var existing = await GetOrThrowAsync(id, ct);

    // Las reglas corren ANTES del mapeo: todavía ven el estado previo de la entidad.
    await rules.EnsureCanUpdateAsync(id, dto, existing, ct);

    mapper.Map(dto, existing); // mapea SOBRE la entidad rastreada
    await repository.UpdateAsync(existing, ct);
  }

  public async Task DeleteAsync(int id, CancellationToken ct = default)
  {
    var existing = await GetOrThrowAsync(id, ct);

    await rules.EnsureCanDeleteAsync(existing, ct);

    await repository.DeleteAsync(id, ct);
  }

  /// <summary>
  /// Carga la entidad rastreada o lanza el 404 de dominio. Es el único sitio del
  /// proyecto donde nace un <see cref="NotFoundAppException"/> por id.
  /// </summary>
  private async Task<TEntity> GetOrThrowAsync(int id, CancellationToken ct)
      => await repository.GetByIdAsync(id, ct)
         ?? throw new NotFoundAppException(rules.EntityName, id);

}
