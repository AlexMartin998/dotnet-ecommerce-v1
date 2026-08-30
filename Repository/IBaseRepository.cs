using ApiEcommerce.Models;
using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Repository;


/// <summary>
/// Contrato CRUD genérico sobre una entidad. Equivalente a
/// <c>JpaRepository&lt;T, Integer&gt;</c> de Spring Data.
/// </summary>
/// <remarks>
/// <b>El repositorio nunca lanza excepciones de negocio.</b> Un id inexistente
/// devuelve <c>null</c>/<c>false</c> y un listado sin resultados devuelve una
/// colección vacía; quien decide que "no encontrado" es un 404 es el servicio.
/// </remarks>
/// <typeparam name="T">Entidad EF Core con clave primaria entera.</typeparam>
public interface IBaseRepository<T> where T : class, IEntity
{

  /// <summary>
  /// Devuelve la entidad <b>rastreada</b> por el change tracker, o <c>null</c>.
  /// Es deliberado que rastree: el update del servicio hace
  /// <c>_mapper.Map(dto, existing)</c> sobre esta misma instancia.
  /// </summary>
  Task<T?> GetByIdAsync(int id, CancellationToken ct = default);

  /// <summary>Listado completo sin rastrear, ordenado por <c>CreatedAt</c> descendente (o por <c>Id</c> si la entidad no es <see cref="IAuditable"/>).</summary>
  Task<IEnumerable<T>> GetAllAsync(CancellationToken ct = default);

  /// <summary>
  /// Una página del listado, con el total de filas. Mismo orden que
  /// <see cref="GetAllAsync"/>: un <c>Skip</c>/<c>Take</c> sobre una consulta sin
  /// orden estable puede devolver la misma fila en dos páginas y saltarse otra.
  /// </summary>
  /// <param name="page">Página, base 1.</param>
  /// <param name="pageSize">Tamaño de página, ya validado por el DTO.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<PagedResult<T>> GetPagedAsync(int page, int pageSize, CancellationToken ct = default);

  Task<T> AddAsync(T entity, CancellationToken ct = default);

  Task<T> UpdateAsync(T entity, CancellationToken ct = default);

  /// <summary>Borra por id. Si no existe es un no-op silencioso, no un throw.</summary>
  Task DeleteAsync(int id, CancellationToken ct = default);

  Task<bool> ExistsAsync(int id, CancellationToken ct = default);

  /// <summary>
  /// Confirma los cambios pendientes del contexto.
  /// </summary>
  /// <remarks>
  /// Normalmente no hace falta: cada escritura del repositorio ya guarda. Existe para
  /// el caso en que el servicio añade algo al contexto que <b>debe</b> confirmarse en
  /// la misma transacción (hoy, la fila del outbox junto al descuento de stock).
  /// </remarks>
  Task SaveChangesAsync(CancellationToken ct = default);

  /// <summary>
  /// Comprobación de unicidad genérica sobre un campo <c>string</c>, resuelta por
  /// reflexión sobre el modelo de EF. Útil cuando la entidad no tiene un método
  /// dedicado; <b>si lo tiene (p. ej. <c>NameExistsAsync</c>), se usa el dedicado</b>:
  /// es más rápido y no depende de un string mágico.
  /// </summary>
  /// <param name="fieldName">Nombre de la propiedad. Si no existe o no es <c>string</c>, devuelve <c>false</c>.</param>
  /// <param name="value">Valor a buscar; la comparación es case-insensitive y traducible a SQL.</param>
  /// <param name="excludeId">Id a excluir de la búsqueda (para validar unicidad en un update).</param>
  /// <param name="ct">Token de cancelación propagado hasta EF Core.</param>
  Task<bool> ExistsByFieldAsync(string fieldName, string value, int? excludeId = null, CancellationToken ct = default);

}
