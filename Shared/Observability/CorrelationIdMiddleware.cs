using System.Diagnostics;
using Serilog.Context;

namespace ApiEcommerce.Shared.Observability;


/// <summary>
/// Da a cada petición un identificador que aparece en <b>todas</b> sus líneas de log y
/// viaja de vuelta al cliente en la cabecera <c>X-Correlation-Id</c>.
/// </summary>
/// <remarks>
/// <para>
/// Es lo primero que se pide en un incidente: "mándame el id de la petición que falló".
/// Sin esto, correlacionar los logs de una petición concreta entre réplicas es imposible,
/// y el cliente que reporta el fallo no tiene <b>nada</b> que dar salvo la hora.
/// </para>
/// <para>
/// Si el cliente ya manda uno, <b>se respeta</b>: así una cadena de servicios comparte el
/// mismo identificador de punta a punta. Se acota su longitud porque acaba en el log, y
/// un valor de 10 KB elegido por el cliente es un vector de ruido barato.
/// </para>
/// <para>
/// Convive con el <c>TraceId</c> de OpenTelemetry sin sustituirlo: el <c>TraceId</c> es
/// para las herramientas, este es para las personas y para las cabeceras.
/// </para>
/// </remarks>
public sealed class CorrelationIdMiddleware(RequestDelegate next)
{
  public const string HeaderName = "X-Correlation-Id";

  private const int MaxLength = 128;

  public async Task InvokeAsync(HttpContext context)
  {
    var correlationId = Incoming(context) ?? context.TraceIdentifier;

    context.Items[HeaderName] = correlationId;

    // Se escribe ANTES de seguir: si se hiciera al terminar, una respuesta ya empezada a
    // enviar no admitiría cabeceras nuevas y el id no llegaría justo en los casos lentos.
    context.Response.OnStarting(() =>
    {
      context.Response.Headers[HeaderName] = correlationId;
      return Task.CompletedTask;
    });

    // LogContext lo añade a todas las líneas emitidas dentro de este ámbito, incluidas
    // las del middleware de errores.
    //
    // Y de paso el TraceId de OpenTelemetry, que es lo que permite saltar de una línea de
    // log a la traza completa de esa petición. Se lee de `Activity.Current` en vez de
    // añadir un paquete enriquecedor: son dos líneas y una dependencia menos que mantener.
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
