using ApiEcommerce.Models;

namespace ApiEcommerce.Service.Auth;


/// <summary>
/// Emisor de access tokens. Aislado en su propia interfaz para que
/// <see cref="AuthService"/> no sepa nada de <c>System.IdentityModel.Tokens.Jwt</c>:
/// el día que el token pase a ser opaco/de referencia, cambia esta implementación
/// y nada más.
/// </summary>
public interface IJwtTokenService
{
  /// <summary>
  /// Firma un JWT para el usuario con sus roles ya resueltos.
  /// </summary>
  /// <param name="user">Usuario autenticado.</param>
  /// <param name="roles">Roles efectivos, tal como los devuelve <c>UserManager.GetRolesAsync</c>.</param>
  /// <returns>El token serializado y el instante en que expira.</returns>
  (string Token, DateTime ExpiresAt) CreateToken(ApplicationUser user, IEnumerable<string> roles);
}
