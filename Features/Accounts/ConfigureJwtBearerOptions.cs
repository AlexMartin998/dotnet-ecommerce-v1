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
/// <para>
/// Antes esto era un lambda dentro de <c>AddJwtBearer(...)</c> que volvía a leer la
/// sección con <c>configuration.GetSection("Jwt").Get&lt;JwtOptions&gt;()!</c>. Funcionaba,
/// pero por coincidencia: el <c>!</c> solo era seguro porque <c>ValidateOnStart</c>
/// aborta el arranque antes de que ese lambda llegue a ejecutarse. Bindear dos veces
/// la misma sección es además tener dos fuentes de verdad, una validada y otra no.
/// </para>
/// <para>
/// Con <c>IConfigureNamedOptions&lt;JwtBearerOptions&gt;</c> la configuración entra por el
/// constructor, tipada y validada, igual que en cualquier otro servicio.
/// </para>
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

  public void Configure(JwtBearerOptions options)
  {
    options.SaveToken = true;

    options.TokenValidationParameters = new TokenValidationParameters
    {
      // Las CUATRO validaciones activas. El código de referencia apagaba
      // ValidateIssuer y ValidateAudience porque su token no emitía `iss`/`aud`:
      // eso es parchear el síntoma. Un token firmado con la misma clave por
      // cualquier otro servicio sería aceptado aquí.
      ValidateIssuerSigningKey = true,
      IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(_jwt.SecretKey)),

      ValidateIssuer = true,
      ValidIssuer = _jwt.Issuer,

      ValidateAudience = true,
      ValidAudience = _jwt.Audience,

      ValidateLifetime = true,
      // El default son 5 minutos de gracia: un token expirado seguiría valiendo
      // 5 minutos más. Con reloj sincronizado, 30 s sobra.
      ClockSkew = TimeSpan.FromSeconds(30)
    };

    // Firma buena y sin expirar NO significa "sigue valiendo": un logout puede haberlo
    // invalidado antes de tiempo. Es el precio de que un JWT sea autocontenido — no hay
    // estado del lado del servidor a menos que se añada aquí.
    options.Events = new JwtBearerEvents
    {
      OnTokenValidated = async context =>
      {
        var tokenId = context.Principal?.GetTokenId();

        if (string.IsNullOrEmpty(tokenId)) return;

        var denylist = context.HttpContext.RequestServices.GetRequiredService<IAccessTokenDenylist>();

        if (await denylist.IsRevokedAsync(tokenId, context.HttpContext.RequestAborted))
          // `Fail` y no una excepción: el pipeline lo convierte en un 401 limpio, que es
          // lo que el cliente debe ver — su token ya no vale, punto.
          context.Fail("The access token has been revoked.");
      }
    };
  }
}
