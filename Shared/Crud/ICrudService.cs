using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Shared.Crud;


/// <summary>
/// Contrato CRUD de la capa de servicio: DTO de entrada, DTO de salida. Lo heredan las
/// interfaces por entidad (<c>ICategoryService</c>, <c>IProductService</c>).
/// </summary>
/// <remarks>
/// No menciona la entidad a propósito: un controller que depende de
/// <c>ICategoryService</c> no puede ni ver <c>Category</c>.
/// </remarks>
/// <typeparam name="TDto">DTO de lectura.</typeparam>
/// <typeparam name="TCreateDto">DTO de creación.</typeparam>
/// <typeparam name="TUpdateDto">DTO de actualización parcial.</typeparam>
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
  /// Obtiene por id, o lanza <c>NotFoundAppException</c> si no existe.
  /// </summary>
  /// <remarks>
  /// Lanzar en vez de devolver <c>null</c> hace que el 404 salga igual desde cualquier
  /// endpoint sin depender de que cada controller se acuerde de comprobarlo.
  /// </remarks>
  Task<TDto> GetByIdAsync(int id, CancellationToken ct = default);

  /// <summary>Crea y devuelve el id generado (para el <c>CreatedAtRoute</c> del controller).</summary>
  Task<int> CreateAsync(TCreateDto dto, CancellationToken ct = default);

  /// <summary>Actualización parcial (PATCH) sobre la entidad rastreada.</summary>
  Task UpdateAsync(int id, TUpdateDto dto, CancellationToken ct = default);

  /// <summary>Borra por id, o lanza <c>NotFoundAppException</c> si no existe.</summary>
  Task DeleteAsync(int id, CancellationToken ct = default);

}
