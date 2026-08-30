
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Accounts.Dtos;
namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>
/// Casos de uso de autenticación. No hereda de <c>ICrudService</c> a propósito:
/// un usuario no es un recurso CRUD más — no se "crea", se <b>registra</b>; no se
/// "actualiza", cambia de contraseña o de rol.
/// </summary>
public interface IAuthService
{
  /// <summary>
  /// Registra un usuario nuevo con el rol <c>user</c> y devuelve ya su token
  /// (evita el viaje extra de registrarse y volver a hacer login).
  /// </summary>
  /// <exception cref="Exceptions.ConflictAppException">El username o el email ya existen.</exception>
  /// <exception cref="Exceptions.ValidationAppException">La contraseña no cumple la política de Identity.</exception>
  Task<AuthResponseDto> RegisterAsync(RegisterUserDto dto, CancellationToken ct = default);

  /// <summary>
  /// Valida credenciales y emite el access token.
  /// </summary>
  /// <exception cref="Exceptions.UnauthorizedAppException">Credenciales inválidas o cuenta bloqueada.</exception>
  Task<AuthResponseDto> LoginAsync(LoginUserDto dto, CancellationToken ct = default);

  /// <summary>Perfil del usuario cuyo id viaja en el token (<c>GET /auth/me</c>).</summary>
  /// <exception cref="Exceptions.NotFoundAppException">El id del token ya no existe en la base.</exception>
  Task<UserDto> GetProfileAsync(string userId, CancellationToken ct = default);
}
