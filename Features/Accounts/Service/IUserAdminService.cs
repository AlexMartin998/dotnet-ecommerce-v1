using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Shared.Paging;

namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>Administración de cuentas: listar, dar y quitar roles, bloquear y desbloquear.</summary>
/// <remarks>
/// Se apoya en <c>UserManager</c> y no en el CRUD genérico: <c>ApplicationUser</c> no implementa
/// <c>IEntity</c> y su ciclo de vida pertenece a Identity.
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
  /// El rol no existe. Se comprueba antes para no crear roles fantasma que ningún
  /// <c>[Authorize]</c> nombra y que por tanto no protegen nada.
  /// </exception>
  Task AssignRoleAsync(string userId, string role, string actingAdminId, CancellationToken ct = default);

  /// <summary>Quita un rol.</summary>
  /// <exception cref="Exceptions.ConflictAppException">
  /// Sería quitarse a uno mismo el rol de administrador, o dejar al sistema sin ninguno.
  /// </exception>
  Task RemoveRoleAsync(string userId, string role, string actingAdminId, CancellationToken ct = default);

  /// <summary>Bloquea una cuenta indefinidamente y corta sus sesiones abiertas.</summary>
  /// <remarks>
  /// Revocar los refresh tokens no es un extra: renovar no vuelve a pedir credenciales, así que
  /// sin eso una cuenta bloqueada seguiría renovando su sesión indefinidamente.
  /// </remarks>
  /// <exception cref="Exceptions.ConflictAppException">Es uno mismo.</exception>
  Task LockAsync(string userId, string actingAdminId, CancellationToken ct = default);

  /// <summary>Levanta el bloqueo.</summary>
  Task UnlockAsync(string userId, string actingAdminId, CancellationToken ct = default);
}
