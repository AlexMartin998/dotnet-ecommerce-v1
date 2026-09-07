using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Identity;
using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Features.Accounts.Models;

namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>Registro, login, perfil y cambio de contraseña sobre ASP.NET Core Identity.</summary>
/// <remarks>
/// Aquí no se hashea nada a mano: <c>UserManager.CreateAsync</c> aplica PBKDF2 con salt por
/// usuario y el conteo de iteraciones vigente.
/// </remarks>
public sealed class AuthService(
    UserManager<ApplicationUser> userManager,
    SignInManager<ApplicationUser> signInManager,
    IJwtTokenService tokenService,
    ILogger<AuthService> logger) : IAuthService
{

  public async Task<AuthResponseDto> RegisterAsync(RegisterUserDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    var username = dto.Username.Trim();
    var email = dto.Email.Trim();

    // 409 y no 400: el request es válido en sí mismo, choca con el estado de la base.
    // Quien garantiza la unicidad son los índices únicos de username y email; esto es el mensaje bonito.
    if (await userManager.FindByNameAsync(username) is not null)
      throw new ConflictAppException($"Username '{username}' is already taken.");

    if (await userManager.FindByEmailAsync(email) is not null)
      throw new ConflictAppException($"Email '{email}' is already registered.");

    var user = new ApplicationUser
    {
      UserName = username,
      Email = email,
      Name = dto.Name?.Trim()
    };

    var created = await userManager.CreateAsync(user, dto.Password);

    // El duplicado que se cuela por la carrera sigue siendo 409, como el que ve la comprobación.
    if (created.Errors.FirstOrDefault(IsDuplicate) is { } duplicate)
      throw new ConflictAppException(duplicate.Description);

    // 422 y no 400: el DTO era válido, lo que no se cumple es la política de contraseñas.
    if (!created.Succeeded)
      throw new ValidationAppException(ToFieldErrors(created));

    var addedToRole = await userManager.AddToRoleAsync(user, Roles.User);

    if (!addedToRole.Succeeded)
    {
      // Sin rol la cuenta quedaría a medias: se deshace la creación.
      await userManager.DeleteAsync(user);
      throw new ValidationAppException(ToFieldErrors(addedToRole));
    }

    logger.LogInformation("User {Username} registered with role {Role}", username, Roles.User);

    return await BuildAuthResponseAsync(user);
  }

  public async Task<AuthResponseDto> LoginAsync(LoginUserDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    var username = dto.Username.Trim();
    var user = await userManager.FindByNameAsync(username);

    // Mismo mensaje que para contraseña incorrecta: distinguirlos permite enumerar usuarios.
    if (user is null)
    {
      logger.LogWarning("Failed login attempt for unknown username {Username}", username);
      throw new UnauthorizedAppException("Invalid username or password.");
    }

    // Sin lockoutOnFailure: true, Identity nunca cuenta los fallos y el bloqueo no se activa.
    var result = await signInManager.CheckPasswordSignInAsync(user, dto.Password, lockoutOnFailure: true);

    if (result.IsLockedOut)
      throw new ForbiddenAppException("Account temporarily locked after too many failed attempts.");

    if (!result.Succeeded)
    {
      logger.LogWarning("Failed login attempt for user {Username}", username);
      throw new UnauthorizedAppException("Invalid username or password.");
    }

    logger.LogInformation("User {Username} logged in", username);

    return await BuildAuthResponseAsync(user);
  }

  public async Task<UserDto> GetProfileAsync(string userId, CancellationToken ct = default)
  {
    var user = await userManager.FindByIdAsync(userId)
        ?? throw new NotFoundAppException("User", userId);

    return await ToDtoAsync(user);
  }

  public async Task ChangePasswordAsync(
      string userId, ChangePasswordDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    var user = await userManager.FindByIdAsync(userId)
        ?? throw new NotFoundAppException("User", userId);

    var result = await userManager.ChangePasswordAsync(user, dto.CurrentPassword, dto.NewPassword);

    if (result.Succeeded)
    {
      logger.LogInformation("User {UserId} changed their password", userId);
      return;
    }

    // Se distingue "la actual no es correcta" de "la nueva no cumple la política": no es un
    // oráculo, porque para llegar aquí ya hay que estar autenticado como ese usuario.
    if (result.Errors.Any(e => e.Code == "PasswordMismatch"))
      throw new UnauthorizedAppException("The current password is not correct.");

    throw new ValidationAppException(new Dictionary<string, string[]>
    {
      ["newPassword"] = [.. result.Errors.Select(e => e.Description)]
    });
  }

  // ---- helpers privados ---------------------------------------------------

  private async Task<AuthResponseDto> BuildAuthResponseAsync(ApplicationUser user)
  {
    var roles = await userManager.GetRolesAsync(user);
    var (token, expiresAt) = tokenService.CreateToken(user, roles);

    return new AuthResponseDto
    {
      Token = token,
      ExpiresAt = expiresAt,
      User = ToDto(user, roles)
    };
  }

  private async Task<UserDto> ToDtoAsync(ApplicationUser user)
      => ToDto(user, await userManager.GetRolesAsync(user));

  /// <summary>
  /// Proyección a mano y no AutoMapper: los roles son una consulta aparte, y un Profile
  /// tendría que inyectar el <c>UserManager</c> para resolverlos.
  /// </summary>
  private static UserDto ToDto(ApplicationUser user, IEnumerable<string> roles) => new()
  {
    Id = user.Id,
    Username = user.UserName ?? string.Empty,
    Email = user.Email,
    Name = user.Name,
    Roles = [.. roles],
    CreatedAt = user.CreatedAt
  };

  /// <summary>Códigos con los que Identity avisa de un username o email ya usados.</summary>
  private static bool IsDuplicate(IdentityError error)
      => error.Code is "DuplicateUserName" or "DuplicateEmail";

  /// <summary>
  /// Agrupa los <c>IdentityError</c> en el formato <c>{ campo: [mensajes] }</c> de
  /// <c>ValidationProblem(ModelState)</c>, para que el cliente vea siempre la misma forma.
  /// </summary>
  private static Dictionary<string, string[]> ToFieldErrors(IdentityResult result)
      => result.Errors
          .GroupBy(e => FieldFor(e.Code))
          .ToDictionary(g => g.Key, g => g.Select(e => e.Description).ToArray());

  private static string FieldFor(string identityErrorCode) => identityErrorCode switch
  {
    var c when c.Contains("Password", StringComparison.Ordinal) => nameof(RegisterUserDto.Password),
    var c when c.Contains("Email", StringComparison.Ordinal) => nameof(RegisterUserDto.Email),
    var c when c.Contains("UserName", StringComparison.Ordinal) => nameof(RegisterUserDto.Username),
    _ => "request"
  };
}
