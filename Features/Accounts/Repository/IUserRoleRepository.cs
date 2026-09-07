namespace ApiEcommerce.Features.Accounts.Repository;


/// <summary>Consultas y bloqueos sobre la asignación de roles que <c>UserManager</c> no cubre.</summary>
/// <remarks>
/// No hereda de <c>IBaseRepository&lt;T&gt;</c>: quien escribe en las tablas de Identity sigue
/// siendo <c>UserManager</c>, aquí solo está lo que hace falta para que arbitre la base.
/// </remarks>
public interface IUserRoleRepository
{
  /// <summary>Cuántos usuarios tienen el rol indicado.</summary>
  Task<int> CountUsersInRoleAsync(string role, CancellationToken ct = default);

  /// <summary>Serializa las bajas del rol administrador. Exige una transacción abierta.</summary>
  /// <remarks>
  /// El recuento de administradores y la baja son dos viajes a la base: sin este bloqueo, dos
  /// peticiones simultáneas cuentan dos y se quitan el rol las dos, dejando el sistema sin nadie.
  /// </remarks>
  /// <returns><c>false</c> si otra petición lo tenía y no se concedió a tiempo.</returns>
  Task<bool> TryLockAdminRoleAsync(TimeSpan timeout, CancellationToken ct = default);
}
