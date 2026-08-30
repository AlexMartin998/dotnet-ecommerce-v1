using ApiEcommerce.Exceptions;
using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Identity;

namespace ApiEcommerce.Service.Auth;


/// <summary>
/// Registro y login sobre ASP.NET Core Identity.
/// </summary>
/// <remarks>
/// <para>
/// <b>Aquí no se hashea nada a mano.</b> <c>UserManager.CreateAsync(user, password)</c>
/// aplica PBKDF2 con salt por usuario y el conteo de iteraciones vigente; cualquier
/// <c>SHA256(password)</c> casero que se vea en un tutorial es una vulnerabilidad.
/// </para>
/// <para>
/// Mapa mental desde Spring Security: <c>UserManager</c> ≈ <c>UserDetailsService</c> +
/// <c>PasswordEncoder</c>, <c>SignInManager</c> ≈ <c>AuthenticationManager</c>,
/// <c>RoleManager</c> ≈ la gestión de <c>GrantedAuthority</c>.
/// </para>
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

    // 422: la forma del DTO era válida (eso ya lo filtró DataAnnotations), pero la
    // política de contraseñas de Identity no se cumple. Se devuelve campo a campo.
    if (!created.Succeeded)
      throw new ValidationAppException(ToFieldErrors(created));

    var addedToRole = await userManager.AddToRoleAsync(user, Roles.User);

    if (!addedToRole.Succeeded)
    {
      // Si el rol no se pudo asignar, el usuario quedaría creado pero sin permisos:
      // se deshace la creación para no dejar una cuenta a medias.
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

    // Mismo mensaje para "no existe" y "contraseña incorrecta": decir cuál de las dos
    // falló convierte el login en un oráculo para enumerar usuarios.
    if (user is null)
    {
      logger.LogWarning("Failed login attempt for unknown username {Username}", username);
      throw new UnauthorizedAppException("Invalid username or password.");
    }

    // lockoutOnFailure: true activa el bloqueo por intentos fallidos que se configura
    // en AddIdentity(options.Lockout...). Sin este flag, Identity nunca cuenta fallos.
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
  /// Proyección a mano en vez de AutoMapper: los roles no son una propiedad de
  /// <see cref="ApplicationUser"/> sino una consulta aparte, así que un Profile
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

  /// <summary>
  /// Agrupa los <c>IdentityError</c> por código en el mismo formato
  /// <c>{ campo: [mensajes] }</c> que produce <c>ValidationProblem(ModelState)</c>,
  /// para que el cliente reciba siempre la misma forma de error.
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
