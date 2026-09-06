
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Accounts.Dtos;
namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>Casos de uso de autenticación: registro, login, perfil y cambio de contraseña.</summary>
/// <remarks>
/// No hereda de <c>ICrudService</c>: un usuario no se crea ni se actualiza, se registra y
/// cambia de contraseña o de rol.
/// </remarks>
public interface IAuthService
{
  /// <summary>
  /// Registra un usuario nuevo con el rol <c>user</c> y devuelve ya su token, para ahorrar
  /// el login inmediato.
  /// </summary>
  /// <exception cref="Exceptions.ConflictAppException">El username o el email ya existen.</exception>
  /// <exception cref="Exceptions.ValidationAppException">La contraseña no cumple la política de Identity.</exception>
  Task<AuthResponseDto> RegisterAsync(RegisterUserDto dto, CancellationToken ct = default);

  /// <summary>Valida credenciales y emite el access token.</summary>
  /// <exception cref="Exceptions.UnauthorizedAppException">Credenciales inválidas o cuenta bloqueada.</exception>
  Task<AuthResponseDto> LoginAsync(LoginUserDto dto, CancellationToken ct = default);

  /// <summary>Cambia la contraseña del propio usuario.</summary>
  /// <remarks>
  /// Exige la contraseña actual aunque ya esté autenticado: un access token demuestra que
  /// alguien entró hace un rato, no que quien está delante sea el dueño.
  /// </remarks>
  /// <exception cref="Exceptions.UnauthorizedAppException">La contraseña actual no es correcta.</exception>
  /// <exception cref="Exceptions.ValidationAppException">La nueva no cumple la política de Identity.</exception>
  Task ChangePasswordAsync(string userId, ChangePasswordDto dto, CancellationToken ct = default);

  /// <summary>Perfil del usuario cuyo id viaja en el token (<c>GET /auth/me</c>).</summary>
  /// <exception cref="Exceptions.NotFoundAppException">El id del token ya no existe en la base.</exception>
  Task<UserDto> GetProfileAsync(string userId, CancellationToken ct = default);
}
