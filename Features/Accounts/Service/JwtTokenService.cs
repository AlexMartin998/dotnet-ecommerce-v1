using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ApiEcommerce.Shared.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using ApiEcommerce.Features.Accounts;
using ApiEcommerce.Features.Accounts.Models;

namespace ApiEcommerce.Features.Accounts.Service;


/// <summary>Firma JWTs con HMAC-SHA256 a partir de <see cref="JwtOptions"/>.</summary>
/// <remarks>
/// Singleton: sin estado por request, y la clave de firma se materializa una sola vez en el
/// constructor en vez de en cada login.
/// </remarks>
public sealed class JwtTokenService : IJwtTokenService
{
  private readonly JwtOptions _options;
  private readonly SigningCredentials _credentials;

  /// <summary>Materializa la clave de firma una sola vez.</summary>
  public JwtTokenService(IOptions<JwtOptions> options)
  {
    _options = options.Value;

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
    _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
  }

  /// <inheritdoc/>
  public (string Token, DateTime ExpiresAt) CreateToken(ApplicationUser user, IEnumerable<string> roles)
  {
    ArgumentNullException.ThrowIfNull(user);

    var expiresAt = DateTime.UtcNow.AddMinutes(_options.ExpirationMinutes);

    var claims = new List<Claim>
    {
      // `sub` es el id inmutable; el username puede cambiar y no sirve como clave.
      new(JwtRegisteredClaimNames.Sub, user.Id),
      new(JwtRegisteredClaimNames.UniqueName, user.UserName ?? string.Empty),
      new(JwtRegisteredClaimNames.Email, user.Email ?? string.Empty),

      // jti: identifica el token para poder revocarlo sin invalidar los demás del usuario.
      new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),

      // Duplicados en el formato clásico de .NET, que es el que leen [Authorize] y User.Identity.
      new(ClaimTypes.NameIdentifier, user.Id),
      new(ClaimTypes.Name, user.UserName ?? string.Empty)
    };

    // Un claim `role` por rol: así [Authorize(Roles = "admin,user")] funciona solo.
    claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

    var token = new JwtSecurityToken(
        issuer: _options.Issuer,
        audience: _options.Audience,
        claims: claims,
        // En UTC porque la RFC 7519 define `exp` como epoch UTC: única excepción al reloj local.
        notBefore: DateTime.UtcNow,
        expires: expiresAt,
        signingCredentials: _credentials);

    return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt.ToLocalTime());
  }
}
