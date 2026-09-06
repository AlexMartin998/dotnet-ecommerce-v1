using ApiEcommerce.Features.Accounts.Models;

namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>Emisor de access tokens.</summary>
/// <remarks>
/// Aislado en su propia interfaz para que <see cref="AuthService"/> no dependa de
/// <c>System.IdentityModel.Tokens.Jwt</c> y el formato del token se pueda cambiar aquí solo.
/// </remarks>
public interface IJwtTokenService
{
  /// <summary>Firma un JWT para el usuario con sus roles ya resueltos.</summary>
  /// <param name="user">Usuario autenticado.</param>
  /// <param name="roles">Roles efectivos, tal como los devuelve <c>UserManager.GetRolesAsync</c>.</param>
  /// <returns>El token serializado y el instante en que expira.</returns>
  (string Token, DateTime ExpiresAt) CreateToken(ApplicationUser user, IEnumerable<string> roles);
}
