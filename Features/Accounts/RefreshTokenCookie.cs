using ApiEcommerce.Features.Accounts.Service;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Accounts;


/// <summary>
/// Pone y quita la cookie del refresh token. Es la costura entre el protocolo y el
/// servicio: <c>IRefreshTokenService</c> devuelve un valor y no sabe qué es una cookie.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Por qué cookie y no cuerpo de la respuesta.</b> Con <c>HttpOnly</c>, el JavaScript
/// de la página <b>no puede leerla</b>: un XSS puede hacer peticiones en nombre del
/// usuario mientras la pestaña esté abierta, pero no puede robarse la sesión y usarla
/// desde otro sitio durante semanas. El access token sí viaja en el cuerpo porque dura 15
/// minutos y no puede renovarse solo; el refresh es el que de verdad hay que proteger.
/// </para>
/// <para>
/// El precio, que conviene tener presente: exige CORS con credenciales, obliga a HTTPS y
/// complica a un cliente móvil o de escritorio, que no tiene cookies de balde.
/// </para>
/// </remarks>
public sealed class RefreshTokenCookie(IOptions<RefreshTokenOptions> options)
{
  private readonly RefreshTokenOptions _options = options.Value;

  public string? Read(HttpRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    return request.Cookies.TryGetValue(_options.CookieName, out var value) ? value : null;
  }

  public void Write(HttpResponse response, IssuedRefreshToken token)
  {
    ArgumentNullException.ThrowIfNull(response);

    response.Cookies.Append(_options.CookieName, token.Token, Build(response, token.ExpiresAt));
  }

  /// <summary>Borra la cookie del navegador.</summary>
  /// <remarks>
  /// ⚠️ Con <b>las mismas opciones</b> con las que se escribió. Un `Delete` con Path o
  /// SameSite distintos no borra nada: el navegador lo trata como otra cookie y la
  /// original sigue ahí. Es un fallo silencioso clásico del logout.
  /// </remarks>
  public void Clear(HttpResponse response)
  {
    ArgumentNullException.ThrowIfNull(response);

    response.Cookies.Delete(_options.CookieName, Build(response, DateTime.UnixEpoch));
  }

  private CookieOptions Build(HttpResponse response, DateTime expiresAt) => new()
  {
    // Que el JS de la página no pueda leerla es la razón entera de usar cookie.
    HttpOnly = true,

    // ⚠️ Se decide por el ESQUEMA de la petición, no por el entorno. Una cookie `Secure`
    // sobre HTTP el cliente **ni la guarda**: el login respondería 200, la cookie no se
    // guardaría y el refresh fallaría SIEMPRE, sin un solo error en el servidor.
    //
    // La primera versión miraba `IsDevelopment()`, y eso rompía en cuanto el entorno se
    // llamaba de otra forma: el host de tests usa "Testing" y sirve por HTTP, así que
    // marcaba la cookie como Secure y ningún test de sesión podía pasar. Lo cazaron ellos.
    //
    // Con el esquema real se ajusta solo: en producción se sirve por HTTPS (hay HSTS), así
    // que la cookie sale `Secure`; en local y en los tests, que van por HTTP, no.
    // ⚠️ Detrás de un proxy TLS esto depende de `UseForwardedHeaders`, que ya está puesto:
    // sin él, `IsHttps` sería false y la cookie viajaría sin `Secure` en producción.
    Secure = response.HttpContext.Request.IsHttps,

    // Strict: el navegador no la manda en peticiones que vengan de otro sitio, que es
    // lo que corta el CSRF de raíz. `Lax` bastaría para navegación de primer nivel, pero
    // aquí no hace falta: el refresh siempre lo dispara la propia aplicación.
    SameSite = SameSiteMode.Strict,

    // Solo se manda a los endpoints que la necesitan. No hay razón para que viaje en
    // cada petición al catálogo.
    Path = "/api/v1/auth",

    Expires = expiresAt
  };
}
