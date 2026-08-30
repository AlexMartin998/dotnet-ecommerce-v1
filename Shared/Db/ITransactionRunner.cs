namespace ApiEcommerce.Shared.Db;


/// <summary>
/// Ejecuta una unidad de trabajo dentro de una transacción, compatible con la
/// estrategia de reintentos de EF Core.
/// </summary>
/// <remarks>
/// <para>
/// Existe porque <c>[Transactional]</c> <b>no puede</b> hacer esto bien. Con
/// <c>EnableRetryOnFailure</c> activo, EF exige que toda la transacción vaya dentro de
/// <c>strategy.ExecuteAsync(...)</c>, y esa estrategia <b>reejecuta el delegado</b> ante
/// un fallo transitorio. El delegado de un filtro de acción
/// (<c>ActionExecutionDelegate</c>) <b>no es reentrante</b>: invocarlo dos veces
/// corrompe el request. Aquí, en cambio, la unidad es una lambda del servicio, que sí
/// se puede repetir.
/// </para>
/// <para>
/// <b>Contrato: la operación DEBE ser replayable.</b> No puede asumir nada del intento
/// anterior — por eso se limpia el change tracker antes de cada intento. Si necesita
/// datos de la base, que los relea dentro de la lambda.
/// </para>
/// <para>
/// La transacción es política de negocio ("descontar stock y emitir el evento son
/// atómicos"), no de HTTP, así que vive en el servicio y no en el controller. Y el
/// servicio sigue sin ver <c>AppDbContext</c>: solo esta interfaz.
/// </para>
/// </remarks>
public interface ITransactionRunner
{
  /// <summary>Ejecuta <paramref name="operation"/> en una transacción y devuelve su resultado.</summary>
  Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);
}
