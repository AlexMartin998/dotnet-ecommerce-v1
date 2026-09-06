using Microsoft.Extensions.Options;
using OpenTelemetry.Metrics;
using OpenTelemetry.Resources;
using OpenTelemetry.Trace;
using ApiEcommerce.Shared.Idempotency;

namespace ApiEcommerce.Shared.Observability;


/// <summary>Registro en DI de las trazas y métricas de la aplicación.</summary>
public static class ObservabilityExtensions
{
  /// <summary>
  /// Trazas y métricas con OpenTelemetry: se instrumenta siempre, se exporta solo si hay
  /// a dónde.
  /// </summary>
  /// <remarks>
  /// Sin recolector las trazas siguen existiendo en proceso y alimentan el <c>TraceId</c>
  /// de los logs. Se elige OpenTelemetry y no un SDK de proveedor para que cambiar de
  /// backend sea cambiar un endpoint y no reinstrumentar la aplicación.
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

    // El `.ValidateOnStart()` de arriba no cubre esto: se dispara al resolver
    // `IOptions<ObservabilityOptions>` y aquí se lee el POCO en crudo. Se avisa sin lanzar,
    // porque la observabilidad no puede impedir que la API arranque.
    if (!string.IsNullOrWhiteSpace(options.OtlpEndpoint) && !options.ExportsTraces)
      Console.Error.WriteLine(
          $"[observability] Observability:OtlpEndpoint ('{options.OtlpEndpoint}') is not an " +
          "absolute URI; telemetry will be collected in-process but NOT exported.");

    // Mismo motivo: un valor fuera de rango reventaba con una excepción cruda de terceros.
    var samplingRatio = Math.Clamp(options.SamplingRatio, 0d, 1d);
    var serviceName = string.IsNullOrWhiteSpace(options.ServiceName) ? "apiecommerce" : options.ServiceName;

    services.AddOpenTelemetry()
        .ConfigureResource(resource => resource.AddService(serviceName))
        .WithTracing(tracing =>
        {
          tracing
              // Muestreo padre-consciente: decidir por nuestra cuenta partiría las trazas
              // distribuidas por la mitad.
              .SetSampler(new ParentBasedSampler(new TraceIdRatioBasedSampler(samplingRatio)))
              .AddAspNetCoreInstrumentation(instrumentation =>
              {
                // Las sondas son ruido: corren cada pocos segundos y ahogarían las trazas
                // que importan.
                instrumentation.Filter = context =>
                    !context.Request.Path.StartsWithSegments("/health");

                instrumentation.RecordException = true;
              })
              .AddHttpClientInstrumentation()
              // Las consultas SQL son la mitad de la latencia de esta API. El texto de la
              // consulta no se captura, y su bandera experimental no debe activarse: los
              // parámetros llevarían correos, nombres y precios al backend de trazas.
              .AddSqlClientInstrumentation(instrumentation => instrumentation.RecordException = true);

          if (options.ExportsTraces)
            tracing.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
        })
        .WithMetrics(metrics =>
        {
          metrics
              .AddAspNetCoreInstrumentation()
              .AddHttpClientInstrumentation()
              // Peticiones en curso, latencia, GC y memoria: lo que se mira primero en un
              // incidente.
              .AddRuntimeInstrumentation()
              // La nuestra: cómo se resuelve cada petición con Idempotency-Key.
              .AddMeter(IdempotencyMetrics.MeterName);

          if (options.ExportsTraces)
            metrics.AddOtlpExporter(exporter => exporter.Endpoint = new Uri(options.OtlpEndpoint));
        });

    return services;
  }
}
