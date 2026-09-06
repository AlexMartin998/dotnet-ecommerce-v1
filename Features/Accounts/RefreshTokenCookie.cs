using ApiEcommerce.Features.Accounts.Service;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Accounts;


/// <summary>
/// Pone y quita la cookie del refresh token: la costura entre el protocolo y
/// <c>IRefreshTokenService</c>, que solo devuelve un valor.
/// </summary>
/// <remarks>
/// Va en cookie <c>HttpOnly</c> y no en el cuerpo para que un XSS no pueda robar la sesión.
/// El precio: CORS con credenciales, HTTPS, y clientes nativos que no tienen cookies de balde.
/// </remarks>
public sealed class RefreshTokenCookie(IOptions<RefreshTokenOptions> options)
{
  private readonly RefreshTokenOptions _options = options.Value;

  /// <summary>Lee el refresh token de la cookie, o <c>null</c> si no vino.</summary>
  public string? Read(HttpRequest request)
  {
    ArgumentNullException.ThrowIfNull(request);

    return request.Cookies.TryGetValue(_options.CookieName, out var value) ? value : null;
  }

  /// <summary>Escribe el refresh token recién emitido en la cookie.</summary>
  public void Write(HttpResponse response, IssuedRefreshToken token)
  {
    ArgumentNullException.ThrowIfNull(response);

    response.Cookies.Append(_options.CookieName, token.Token, Build(response, token.ExpiresAt));
  }

  /// <summary>Borra la cookie del navegador.</summary>
  /// <remarks>
  /// Con las mismas opciones con las que se escribió: un <c>Delete</c> con otro Path o
  /// SameSite no borra nada, el navegador lo trata como otra cookie.
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

    // Por el esquema real, no por el entorno: una cookie Secure sobre HTTP el cliente ni la
    // guarda, y el refresh fallaría siempre sin un solo error en el servidor. Detrás de un
    // proxy TLS depende de `UseForwardedHeaders`, que ya está puesto.
    Secure = response.HttpContext.Request.IsHttps,

    // Strict corta el CSRF de raíz; el refresh siempre lo dispara la propia aplicación.
    SameSite = SameSiteMode.Strict,

    // Solo a los endpoints que la necesitan; no tiene que viajar con cada petición al catálogo.
    Path = "/api/v1/auth",

    Expires = expiresAt
  };
}
