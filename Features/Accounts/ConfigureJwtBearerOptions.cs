using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using System.Text;

namespace ApiEcommerce.Features.Accounts;


/// <summary>
/// Configura la validación del Bearer token a partir de las <see cref="JwtOptions"/>
/// <b>ya enlazadas y validadas</b>.
/// </summary>
/// <remarks>
/// Con <c>IConfigureNamedOptions&lt;JwtBearerOptions&gt;</c> la configuración entra por el
/// constructor: un lambda en <c>AddJwtBearer</c> tendría que releer la sección y habría dos
/// fuentes de verdad, una validada y otra no.
/// </remarks>
public sealed class ConfigureJwtBearerOptions(IOptions<JwtOptions> jwtOptions)
  : IConfigureNamedOptions<JwtBearerOptions>
{
  private readonly JwtOptions _jwt = jwtOptions.Value;

  /// <summary>Solo configura el esquema Bearer; otros esquemas no se tocan.</summary>
  public void Configure(string? name, JwtBearerOptions options)
  {
    if (name != JwtBearerDefaults.AuthenticationScheme) return;

    Configure(options);
  }

  /// <summary>Aplica los parámetros de validación del token y la comprobación de denylist.</summary>
  public void Configure(JwtBearerOptions options)
  {
    options.SaveToken = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
      // Las cuatro validaciones activas: sin issuer ni audience, un token firmado con la
      // misma clave por otro servicio sería aceptado aquí.
      ValidateIssuerSigningKey = true,
      IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SecretKey)),

      ValidateIssuer = true,
      ValidIssuer = _jwt.Issuer,

      ValidateAudience = true,
      ValidAudience = _jwt.Audience,

      ValidateLifetime = true,
      // El default de 5 minutos alarga la vida de un token expirado; con reloj sincronizado sobra.
      ClockSkew = TimeSpan.FromSeconds(30)
    };

    // Firma buena y sin expirar no significa vigente: un logout pudo invalidarlo antes.
    options.Events = new JwtBearerEvents
    {
      OnTokenValidated = async context =>
      {
        var tokenId = context.Principal?.GetTokenId();

        if (string.IsNullOrEmpty(tokenId)) return;

        var denylist = context.HttpContext.RequestServices.GetRequiredService<IAccessTokenDenylist>();

        if (await denylist.IsRevokedAsync(tokenId, context.HttpContext.RequestAborted))
          // `Fail` y no una excepción: el pipeline lo convierte en un 401 limpio.
          context.Fail("The access token has been revoked.");
      }
    };
  }
}
