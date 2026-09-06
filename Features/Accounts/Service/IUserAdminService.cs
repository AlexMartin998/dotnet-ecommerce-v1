using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>
/// Administración de cuentas: listar, dar y quitar roles, bloquear y desbloquear.
/// </summary>
/// <remarks>
/// <para>
/// Se apoya en <c>UserManager</c> y <b>no</b> en <c>BaseRepository&lt;T&gt;</c> ni en
/// <c>CrudService&lt;&gt;</c>: <c>ApplicationUser</c> no implementa <c>IEntity</c> —su
/// clave es un <c>string</c>— y su ciclo de vida pertenece a Identity, que es quien sabe
/// de hashes, sellos de seguridad y bloqueos. Forzarlo dentro del CRUD genérico sería
/// meter una entidad en una abstracción que no le sirve.
/// </para>
/// <para>
/// Las reglas de aquí no son validaciones de formato: son las que impiden que un clic
/// deje el sistema sin nadie que pueda administrarlo.
/// </para>
/// </remarks>
public interface IUserAdminService
{
  /// <summary>Listado paginado de usuarios, con sus roles.</summary>
  Task<PagedResult<UserDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default);

  /// <summary>Detalle de un usuario.</summary>
  /// <exception cref="Exceptions.NotFoundAppException">No existe.</exception>
  Task<UserDto> GetByIdAsync(string userId, CancellationToken ct = default);

  /// <summary>Da un rol a un usuario. Idempotente: dárselo dos veces no cambia nada.</summary>
  /// <param name="userId">A quién.</param>
  /// <param name="role">Qué rol. Debe existir.</param>
  /// <param name="actingAdminId">Quién lo hace, para dejar constancia.</param>
  /// <param name="ct">Token de cancelación.</param>
  /// <exception cref="Exceptions.NotFoundAppException">El usuario no existe.</exception>
  /// <exception cref="Exceptions.BadOperationAppException">
  /// El rol no existe. ⚠️ Se comprueba porque <c>UserManager.AddToRoleAsync</c> con un rol
  /// desconocido falla, pero un <c>RoleManager</c> mal usado lo crearía al vuelo: acabaríamos
  /// con roles fantasma que no protegen nada porque ningún <c>[Authorize]</c> los nombra.
  /// </exception>
  Task AssignRoleAsync(string userId, string role, string actingAdminId, CancellationToken ct = default);

  /// <summary>Quita un rol.</summary>
  /// <exception cref="Exceptions.ConflictAppException">
  /// Sería quitarse a uno mismo el rol de administrador, o dejar al sistema sin ninguno.
  /// </exception>
  Task RemoveRoleAsync(string userId, string role, string actingAdminId, CancellationToken ct = default);

  /// <summary>
  /// Bloquea una cuenta indefinidamente y <b>corta sus sesiones abiertas</b>.
  /// </summary>
  /// <remarks>
  /// ⚠️ Lo segundo no es un extra: sin revocar los refresh tokens, bloquear una cuenta no
  /// sirve de nada. El usuario sigue dentro con el access token que ya tiene y —lo grave—
  /// podría <b>seguir renovándolo indefinidamente</b>, porque renovar no vuelve a pedir
  /// credenciales.
  /// </remarks>
  /// <exception cref="Exceptions.ConflictAppException">Es uno mismo.</exception>
  Task LockAsync(string userId, string actingAdminId, CancellationToken ct = default);

  /// <summary>Levanta el bloqueo.</summary>
  Task UnlockAsync(string userId, string actingAdminId, CancellationToken ct = default);
}
