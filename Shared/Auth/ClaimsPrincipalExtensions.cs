using System.Security.Claims;
using System.IdentityModel.Tokens.Jwt;

namespace ApiEcommerce.Shared.Auth;


/// <summary>
/// Lectura del usuario autenticado desde el <see cref="ClaimsPrincipal"/> del request.
/// </summary>
/// <remarks>
/// Equivale a <c>SecurityContextHolder</c> de Spring Security, pero sin estado global: el
/// principal viaja en el request.
/// </remarks>
public static class ClaimsPrincipalExtensions
{
  /// <summary>Id del usuario (claim <c>sub</c> / <c>NameIdentifier</c>), o <c>null</c> si es anónimo.</summary>
  public static string? GetUserId(this ClaimsPrincipal principal)
      => principal.FindFirstValue(ClaimTypes.NameIdentifier);

  /// <summary>Id del usuario, lanzando 401 si el request no está autenticado.</summary>
  /// <remarks>
  /// Solo se usa en acciones con <c>[Authorize]</c>, donde el 401 ya lo habría dado el
  /// middleware: es una red de seguridad ante un atributo olvidado.
  /// </remarks>
  public static string GetRequiredUserId(this ClaimsPrincipal principal)
      => principal.GetUserId()
         ?? throw new Exceptions.UnauthorizedAppException("The token does not carry a user id.");

  /// <summary>Email del usuario autenticado, o <c>null</c> si el token no lo trae.</summary>
  /// <remarks>
  /// Se busca por los dos nombres del mismo claim porque el validador traduce los claims
  /// estándar a URIs de <c>ClaimTypes</c> salvo que se desactive el mapeo: preguntar por
  /// uno solo hace que el email desaparezca en silencio si alguien toca esa bandera.
  /// </remarks>
  public static string? GetEmail(this ClaimsPrincipal principal)
  {
    ArgumentNullException.ThrowIfNull(principal);

    return principal.FindFirstValue(ClaimTypes.Email)
           ?? principal.FindFirstValue(JwtRegisteredClaimNames.Email);
  }

  /// <summary>El <c>jti</c> del access token: lo que identifica a ESTE token, no al usuario.</summary>
  /// <remarks>
  /// Es lo que permite invalidar un token concreto sin tocar los demás del mismo usuario
  /// (ver <c>IAccessTokenDenylist</c>).
  /// </remarks>
  public static string? GetTokenId(this ClaimsPrincipal principal)
  {
    ArgumentNullException.ThrowIfNull(principal);

    return principal.FindFirstValue(JwtRegisteredClaimNames.Jti);
  }

  /// <summary>Cuándo expira el access token, en UTC.</summary>
  /// <remarks>
  /// El claim <c>exp</c> es un epoch UTC (RFC 7519) y es la excepción al <c>DateTime.Now</c>
  /// local del proyecto: convertirlo a local daría a la denylist un TTL con horas de desfase.
  /// </remarks>
  public static DateTime? GetTokenExpiry(this ClaimsPrincipal principal)
  {
    ArgumentNullException.ThrowIfNull(principal);

    var raw = principal.FindFirstValue(JwtRegisteredClaimNames.Exp);

    return long.TryParse(raw, out var seconds)
        ? DateTimeOffset.FromUnixTimeSeconds(seconds).UtcDateTime
        : null;
  }
}
