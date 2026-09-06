using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;

namespace ApiEcommerce.Shared.Observability;


public static class ObservabilityExtensions
{
  /// <summary>
  /// Trazas y métricas con OpenTelemetry.
  /// </summary>
  /// <remarks>
  /// <para>
  /// <b>Se instrumenta siempre; se exporta solo si hay a dónde.</b> Sin recolector, las
  /// trazas siguen existiendo en proceso —y son las que dan el <c>TraceId</c> que aparece
  /// en cada línea de log— pero no se intenta enviar nada. Es la misma decisión que con
  /// Redis y RabbitMQ: la observabilidad no puede ser el motivo de que la API no arranque.
  /// </para>
  /// <para>
  /// Se elige OpenTelemetry y no un SDK de proveedor porque el <b>vendor lock-in de la
  /// observabilidad se paga tarde y caro</b>: cambiar de backend es cambiar un endpoint,
  /// no reinstrumentar la aplicación.
  /// </para>
  /// </remarks>
  public static IServiceCollection AddObservability(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<ObservabilityOptions>()
        .Bind(configuration.GetSection(ObservabilityOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    var options = configuration.GetSection(ObservabilityOptions.SectionName).Get<ObservabilityOptions>()
                  ?? new ObservabilityOptions();

    services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(options.ServiceName))
        .WithTracing(tracing =>
        {
          tracing
              // Muestreo padre-consciente: si el servicio que nos llamó decidió trazar la
              // petición, se traza; si no, se aplica la proporción. Decidir por nuestra
              // cuenta partiría las trazas distribuidas por la mitad.
              .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(options.SamplingRatio)))
              .AddAspNetCoreInstrumentation(instrumentation =>
              {
                // Las sondas son ruido: se ejecutan cada pocos segundos, siempre igual, y
                // ahogarían cualquier traza que importe.
                instrumentation.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health");

                instrumentation.RecordException = true;
              })
              .AddHttpClientInstrumentation()
              // Las consultas SQL son la mitad de la latencia de esta API.
              //
              // ⚠️ El TEXTO de la consulta no se captura, y no hay que hacer nada para
              // ello: desde la 1.10 dejó de ser una propiedad y está apagado salvo que se
              // active la bandera experimental
              // `OTEL_DOTNET_EXPERIMENTAL_SQLCLIENT_ENABLE_TRACE_TEXT_COMMAND`.
              // **No activarla**: llevaría los valores de los parámetros —correos,
              // nombres, precios— al backend de trazas, que casi nunca tiene el mismo
              // nivel de protección que la base de datos.
              .AddSqlClientInstrumentation(instrumentation => instrumentation.RecordException = true);

          if (options.ExportsTraces)
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
        })
        .WithMetrics(metrics =>
        {
          metrics
              .AddAspNetCoreInstrumentation()
              .AddHttpClientInstrumentation()
              // Las cuatro que se miran primero en un incidente: peticiones en curso,
              // latencia, GC y memoria.
              .AddRuntimeInstrumentation();

          if (options.ExportsTraces)
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
        });

    return services;
  }
}
