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

    // Orden explícito: sin él, un usuario puede salir dos veces o ninguna al pasar de página.
    var users = await userManager.Users
        .AsNoTracking()
        .OrderBy(u => u.UserName)
        .Skip(query.Skip)
        .Take(query.PageSize)
        .ToListAsync(ct);

    // N+1 asumido: la página está acotada a 100 y esto es un panel, no un endpoint caliente.
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

    // Idempotente: un 409 aquí obligaría al cliente a consultar antes de cada asignación.
    if (await userManager.IsInRoleAsync(user, normalized)) return;

    var result = await userManager.AddToRoleAsync(user, normalized);

    if (!result.Succeeded) throw Failure(result);

    // Auditoría: sin rastro no hay forma de saber quién concedió un rol meses después.
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
      // Uno no se quita a sí mismo el administrador: se dejaría fuera de su propio panel.
      if (userId == actingAdminId)
        throw new ConflictAppException("An administrator cannot remove their own admin role.");

      // Nunca sin administradores. Va después de la regla anterior para dar el mensaje que toca.
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

    // `MaxValue` = indefinido: Identity solo sabe de fechas de fin, no de bloqueos perpetuos.
    var result = await userManager.SetLockoutEndDateAsync(user, DateTimeOffset.MaxValue);

    if (!result.Succeeded) throw Failure(result);

    // Sin esto el bloqueo no significa nada: el usuario seguiría renovando su sesión.
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

    // Sin limpiar los fallos acumulados, la cuenta se volvería a bloquear al primer error.
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
