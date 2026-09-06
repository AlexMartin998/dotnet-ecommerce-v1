using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Shared.Crud;


/// <summary>
/// Contrato CRUD de la capa de servicio: <b>DTO de entrada, DTO de salida</b>.
/// Es el que heredan las interfaces por entidad (<c>ICategoryService</c>,
/// <c>IProductService</c>), y por eso <b>no menciona la entidad</b>: un controller que
/// depende de <c>ICategoryService</c> no puede ni ver <c>Category</c>.
/// </summary>
public interface ICrudService<TDto, TCreateDto, TUpdateDto>
{

  /// <summary>Listado completo. Sin resultados devuelve colección vacía, nunca 404.</summary>
  Task<IEnumerable<TDto>> GetAllAsync(CancellationToken ct = default);

  /// <summary>
  /// Una página del listado. Un <paramref name="query"/> fuera de rango devuelve una
  /// página vacía con el total real, nunca un 404.
  /// </summary>
  Task<PagedResult<TDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default);

  /// <summary>
  /// Obtiene por id. <b>Lanza <c>NotFoundAppException</c> si no existe</b>, no devuelve
  /// <c>null</c>: así el 404 sale igual desde cualquier endpoint sin depender de que
  /// cada controller se acuerde de comprobarlo.
  /// </summary>
  Task<TDto> GetByIdAsync(int id, CancellationToken ct = default);

  /// <summary>Crea y devuelve el id generado (para el <c>CreatedAtRoute</c> del controller).</summary>
  Task<int> CreateAsync(TCreateDto dto, CancellationToken ct = default);

  /// <summary>Actualización parcial (PATCH) sobre la entidad rastreada.</summary>
  Task UpdateAsync(int id, TUpdateDto dto, CancellationToken ct = default);

  Task DeleteAsync(int id, CancellationToken ct = default);

}
