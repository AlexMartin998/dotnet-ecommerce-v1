using Asp.Versioning;

namespace ApiEcommerce.Shared.Http;


public static class HttpContextExtensions
{
  /// <summary>
  /// Versión de API del request actual, como string, para rellenar el parámetro
  /// <c>{version}</c> al generar URLs con <c>CreatedAtRoute</c>.
  /// </summary>
  /// <remarks>
  /// Sin esto, un <c>CreatedAtRoute("GetCategory", new { id })</c> sobre una ruta
  /// <c>api/v{version:apiVersion}/...</c> depende de que el generador reutilice el
  /// valor ambiente del request. Cuando no lo hace, no falla el 201: falla la
  /// generación de la cabecera <c>Location</c> con un 500 sin relación aparente.
  /// Pasarlo explícito cuesta una palabra y elimina la clase entera de bug.
  /// </remarks>
  public static string ApiVersionValue(this HttpContext context)
      => context.GetRequestedApiVersion()?.ToString() ?? "1.0";
}
