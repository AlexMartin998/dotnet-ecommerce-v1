using System.IdentityModel.Tokens.Jwt;
using System.Security.Claims;
using System.Text;
using ApiEcommerce.Models;
using ApiEcommerce.Shared.Auth;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;

namespace ApiEcommerce.Service.Auth;


/// <summary>
/// Firma JWTs con HMAC-SHA256 a partir de <see cref="JwtOptions"/>.
/// </summary>
/// <remarks>
/// <para>
/// Se registra como <b>Singleton</b>: no toca la base de datos ni guarda estado por
/// request, y la clave de firma se materializa una sola vez en el constructor en vez
/// de en cada login.
/// </para>
/// <para>
/// <c>IOptions&lt;JwtOptions&gt;</c> y no <c>IConfiguration</c>: la configuración ya viene
/// validada (<c>ValidateOnStart</c>) y tipada, así que aquí no hay ni un
/// <c>configuration["Jwt:SecretKey"]</c> que pueda ser <c>null</c>.
/// </para>
/// </remarks>
public sealed class JwtTokenService : IJwtTokenService
{
  private readonly JwtOptions _options;
  private readonly SigningCredentials _credentials;

  public JwtTokenService(IOptions<JwtOptions> options)
  {
    _options = options.Value;

    var key = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_options.SecretKey));
    _credentials = new SigningCredentials(key, SecurityAlgorithms.HmacSha256);
  }

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

      // jti: identificador único del token. Es lo que permitiría revocarlo
      // (denylist en Redis) sin invalidar todos los tokens del usuario.
      new(JwtRegisteredClaimNames.Jti, Guid.NewGuid().ToString()),

      // Duplicados en el formato "clásico" de .NET porque es el que leen
      // [Authorize(Roles = ...)] y User.Identity.Name sin más configuración.
      new(ClaimTypes.NameIdentifier, user.Id),
      new(ClaimTypes.Name, user.UserName ?? string.Empty)
    };

    // Un claim `role` por rol: así [Authorize(Roles = "admin,user")] funciona solo.
    claims.AddRange(roles.Select(role => new Claim(ClaimTypes.Role, role)));

    var token = new JwtSecurityToken(
        issuer: _options.Issuer,
        audience: _options.Audience,
        claims: claims,
        // NotBefore/Expires del JWT van SIEMPRE en UTC: el estándar (RFC 7519)
        // define `exp` como epoch UTC. Es la única excepción al DateTime.Now local
        // del resto del proyecto.
        notBefore: DateTime.UtcNow,
        expires: expiresAt,
        signingCredentials: _credentials);

    return (new JwtSecurityTokenHandler().WriteToken(token), expiresAt.ToLocalTime());
  }
}
