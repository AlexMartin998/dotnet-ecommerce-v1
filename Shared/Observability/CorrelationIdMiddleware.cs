using System.Diagnostics;
using Serilog.Context;

namespace ApiEcommerce.Shared.Observability;


/// <summary>
/// Da a cada petición un identificador que aparece en todas sus líneas de log y viaja de
/// vuelta al cliente en la cabecera <c>X-Correlation-Id</c>.
/// </summary>
/// <remarks>
/// Si el cliente ya manda uno se respeta, para que una cadena de servicios comparta el
/// mismo identificador de punta a punta; su longitud se acota porque acaba en el log.
/// Convive con el <c>TraceId</c> de OpenTelemetry: ese es para las herramientas.
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
  /// <summary>Cabecera que transporta el identificador.</summary>
  public const string HeaderName = "X-Correlation-Id";

  private const int MaxLength = 128;

  /// <summary>Resuelve el identificador, lo publica en la respuesta y en el contexto de log.</summary>
  public async Task InvokeAsync(HttpContext context)
  {
    var correlationId = Incoming(context) ?? context.TraceIdentifier;

    context.Items[HeaderName] = correlationId;

    // Se registra antes de seguir: una respuesta ya empezada a enviar no admite cabeceras
    // nuevas, justo en los casos lentos.
    context.Response.OnStarting(() =>
    {
      context.Response.Headers[HeaderName] = correlationId;
      return Task.CompletedTask;
    });

    // LogContext lo añade a todas las líneas del ámbito, incluidas las del middleware de
    // errores. El TraceId se lee de `Activity.Current` para evitar un paquete enriquecedor.
    using (LogContext.PushProperty("CorrelationId", correlationId))
    using (LogContext.PushProperty("TraceId", Activity.Current?.TraceId.ToString()))
    {
      await next(context);
    }
  }

  private static string? Incoming(HttpContext context)
  {
    var value = context.Request.Headers[HeaderName].ToString();

    return string.IsNullOrWhiteSpace(value) || value.Length > MaxLength ? null : value;
  }
}
