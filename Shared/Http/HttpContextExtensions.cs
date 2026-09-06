using Asp.Versioning;

namespace ApiEcommerce.Shared.Http;


/// <summary>Ayudas de lectura sobre el <see cref="HttpContext"/> del request.</summary>
public static class HttpContextExtensions
{
  /// <summary>
  /// Versión de API del request actual, para rellenar el parámetro <c>{version}</c> al
  /// generar URLs con <c>CreatedAtRoute</c>.
  /// </summary>
  /// <remarks>
  /// Pasarla explícita evita depender de que el generador reutilice el valor ambiente: si
  /// no lo hace, la cabecera <c>Location</c> falla con un 500 sin relación aparente.
  /// </remarks>
  public static string ApiVersionValue(this HttpContext context)
      => context.GetRequestedApiVersion()?.ToString() ?? "1.0";
}
