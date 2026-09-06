using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Observability;


/// <summary>Trazas y métricas (sección <c>Observability</c>).</summary>
public sealed class ObservabilityOptions
{
  public const string SectionName = "Observability";

  /// <summary>Nombre con el que el servicio aparece en el backend de trazas.</summary>
  [Required]
  public string ServiceName { get; init; } = "apiecommerce";

  /// <summary>
  /// Endpoint OTLP del recolector (<c>http://otel-collector:4317</c>).
  /// <b>Vacío = no se exporta nada.</b>
  /// </summary>
  /// <remarks>
  /// Misma decisión que con Redis y RabbitMQ: la observabilidad es una <b>optimización
  /// operativa</b>, no una dependencia dura. Sin recolector la aplicación se instrumenta
  /// igual —las trazas existen en memoria y alimentan el <c>TraceId</c> de los logs— pero
  /// no intenta exportar a un sitio que no está. Una API que no arranca porque falta el
  /// colector de métricas es una API peor.
  /// </remarks>
  public string OtlpEndpoint { get; init; } = string.Empty;

  /// <summary>
  /// Proporción de peticiones muestreadas (0 a 1). En producción trazar el 100% del
  /// tráfico es caro y casi nunca hace falta.
  /// </summary>
  [Range(0d, 1d)]
  public double SamplingRatio { get; init; } = 1d;

  /// <summary>
  /// Endpoint válido y utilizable. <b>Comprueba que sea un URI absoluto</b>, no solo que
  /// no esté vacío.
  /// </summary>
  /// <remarks>
  /// ⚠️ Sin esto, un valor mal escrito (un espacio de más en una variable de entorno)
  /// hacía que <c>new Uri(...)</c> lanzara <c>UriFormatException</c> <b>en el arranque</b>,
  /// y encima <i>después</i> de migrar y sembrar: con <c>restart: unless-stopped</c>, un
  /// crash-loop por un endpoint de métricas. Es exactamente el mismo error de fondo que el
  /// <c>[Required]</c> de <c>SeedOptions.AdminPassword</c>: una pieza opcional decidiendo
  /// si la API arranca.
  /// </remarks>
  public bool ExportsTraces
      => !string.IsNullOrWhiteSpace(OtlpEndpoint)
         && Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out _);
}
