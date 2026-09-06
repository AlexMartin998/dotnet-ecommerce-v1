using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Paging;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Features.Accounts.Service;


/// <inheritdoc cref="IUserAdminService"/>
public sealed class UserAdminService(
    UserManager<ApplicationUser> userManager,
    RoleManager<IdentityRole> roleManager,
    IRefreshTokenService sessions,
    ILogger<UserAdminService> logger) : IUserAdminService
{
  public async Task<PagedResult<UserDto>> GetPagedAsync(
      PageQuery query, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(query);

    var total = await userManager.Users.CountAsync(ct);

    // Orden estable y explícito: sin `OrderBy`, SQL Server no garantiza el mismo orden
    // entre páginas y un usuario puede aparecer dos veces o ninguna al pasar de página.
    var users = await userManager.Users
        .AsNoTracking()
        .OrderBy(u => u.UserName)
        .Skip(query.Skip)
        .Take(query.PageSize)
        .ToListAsync(ct);

    // ⚠️ Una consulta de roles por usuario: es N+1, y se acepta porque la página está
    // acotada a 100 y esto es un panel de administración, no un endpoint caliente. Si
    // algún día molesta, la salida es un JOIN contra UserRoles, no subir el pageSize.
    var items = new List<UserDto>(users.Count);

    foreach (var user in users)
      items.Add(ToDto(user, await userManager.GetRolesAsync(user)));

    return new PagedResult<UserDto>(items, query.Page, query.PageSize, total);
  }

  public async Task<UserDto> GetByIdAsync(string userId, CancellationToken ct = default)
  {
    var user = await FindAsync(userId);

    return ToDto(user, await userManager.GetRolesAsync(user));
  }

  public async Task AssignRoleAsync(
      string userId, string role, string actingAdminId, CancellationToken ct = default)
  {
    var user = await FindAsync(userId);

    var normalized = Normalize(role);

    if (!await roleManager.RoleExistsAsync(normalized))
      throw new BadOperationAppException($"Role '{role}' does not exist.");

    // Idempotente: pedir un rol que ya se tiene no es un error, es que ya está como se
    // quería. Devolver 409 aquí obligaría al cliente a consultar antes de cada asignación.
    if (await userManager.IsInRoleAsync(user, normalized)) return;

    var result = await userManager.AddToRoleAsync(user, normalized);

    if (!result.Succeeded) throw Failure(result);

    // Auditoría: quién, a quién y cuándo. Una promoción a administrador es el cambio de
    // permisos más grande que admite el sistema, y sin rastro no hay forma de responder
    // "¿quién le dio admin a este?" tres meses después.
    logger.LogWarning(
        "ROLE GRANTED: admin {ActingAdminId} granted role {Role} to user {UserId}",
        actingAdminId, normalized, userId);
  }

  public async Task RemoveRoleAsync(
      string userId, string role, string actingAdminId, CancellationToken ct = default)
  {
    var user = await FindAsync(userId);

    var normalized = Normalize(role);

    if (!await userManager.IsInRoleAsync(user, normalized)) return;

    if (normalized == Roles.Admin)
    {
      // Regla 1: uno no se quita a sí mismo el administrador. Es el clic con el que un
      // admin se deja fuera de su propio panel, y además el camino más rápido a dejar el
      // sistema sin nadie que pueda arreglarlo.
      if (userId == actingAdminId)
        throw new ConflictAppException("An administrator cannot remove their own admin role.");

      // Regla 2: nunca sin administradores. Se comprueba DESPUÉS de la anterior para que
      // el mensaje sea el que de verdad explica el rechazo.
      if ((await userManager.GetUsersInRoleAsync(Roles.Admin)).Count <= 1)
        throw new ConflictAppException("The last administrator cannot be demoted.");
    }

    var result = await userManager.RemoveFromRoleAsync(user, normalized);

    if (!result.Succeeded) throw Failure(result);

    logger.LogWarning(
        "ROLE REVOKED: admin {ActingAdminId} removed role {Role} from user {UserId}",
        actingAdminId, normalized, userId);
  }

  public async Task LockAsync(string userId, string actingAdminId, CancellationToken ct = default)
  {
    var user = await FindAsync(userId);

    // Bloquearse a uno mismo deja el panel inaccesible para su propio dueño.
    if (userId == actingAdminId)
      throw new ConflictAppException("An administrator cannot lock their own account.");

    // `MaxValue` = indefinido. Identity no tiene "bloqueado para siempre": tiene una
    // fecha de fin, y el infinito se expresa así.
    var result = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

    if (!result.Succeeded) throw Failure(result);

    // ⚠️ Y AQUÍ está lo que hace que bloquear signifique algo. Sin esto, la cuenta queda
    // marcada como bloqueada y el usuario sigue dentro: su access token vale hasta que
    // expire y —lo grave— podría seguir renovándolo indefinidamente, porque renovar no
    // vuelve a pedir credenciales y por tanto no pasa por el bloqueo.
    await sessions.RevokeAllSessionsAsync(userId, ct);

    logger.LogWarning(
        "ACCOUNT LOCKED: admin {ActingAdminId} locked user {UserId} and revoked their sessions",
        actingAdminId, userId);
  }

  public async Task UnlockAsync(string userId, string actingAdminId, CancellationToken ct = default)
  {
    var user = await FindAsync(userId);

    var result = await userManager.SetLockoutEndDateAsync(user, null);

    if (!result.Succeeded) throw Failure(result);

    // Los intentos fallidos acumulados también se limpian: si no, la cuenta recién
    // desbloqueada se volvería a bloquear al primer error de contraseña.
    await userManager.ResetAccessFailedCountAsync(user);

    logger.LogWarning(
        "ACCOUNT UNLOCKED: admin {ActingAdminId} unlocked user {UserId}", actingAdminId, userId);
  }

  // ---- helpers ------------------------------------------------------------

  private async Task<ApplicationUser> FindAsync(string userId)
      => await userManager.FindByIdAsync(userId)
         ?? throw new NotFoundAppException("User", userId);

  /// <summary>Los roles se guardan en minúsculas (ver <c>Roles</c>).</summary>
  private static string Normalize(string role) => role.Trim().ToLowerInvariant();

  /// <summary>Traduce un fallo de Identity a una excepción de dominio.</summary>
  private static ValidationAppException Failure(IdentityResult result)
      => new(new Dictionary<string, string[]>
      {
        ["identity"] = [.. result.Errors.Select(e => e.Description)]
      });

  private static UserDto ToDto(ApplicationUser user, IList<string> roles) => new()
  {
    Id = user.Id,
    Username = user.UserName ?? string.Empty,
    Email = user.Email,
    Name = user.Name,
    Roles = [.. roles],
    CreatedAt = user.CreatedAt
  };
}
