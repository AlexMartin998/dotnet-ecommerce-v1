namespace ApiEcommerce.Shared.Db;


/// <summary>
/// Ejecuta una unidad de trabajo dentro de una transacción, compatible con la estrategia
/// de reintentos de EF Core.
/// </summary>
/// <remarks>
/// La operación debe ser replayable: la estrategia reejecuta el delegado ante un fallo
/// transitorio y el change tracker se limpia antes de cada intento, así que no puede
/// asumir nada del anterior. La transacción es política de negocio, así que vive aquí.
/// </remarks>
public interface ITransactionRunner
{
  /// <summary>Ejecuta <paramref name="operation"/> en una transacción y devuelve su resultado.</summary>
  Task<T> ExecuteAsync<T>(Func<CancellationToken, Task<T>> operation, CancellationToken ct = default);
}
