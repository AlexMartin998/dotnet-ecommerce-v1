using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Observability;


/// <summary>Trazas y métricas (sección <c>Observability</c>).</summary>
public sealed class ObservabilityOptions
{
  /// <summary>Nombre de la sección de configuración.</summary>
  public const string SectionName = "Observability";

  /// <summary>Nombre con el que el servicio aparece en el backend de trazas.</summary>
  [Required]
  public string ServiceName { get; init; } = "apiecommerce";

  /// <summary>
  /// Endpoint OTLP del recolector (<c>http://otel-collector:4317</c>). Vacío = no se
  /// exporta nada.
  /// </summary>
  /// <remarks>
  /// La observabilidad es una optimización operativa, no una dependencia dura: sin
  /// recolector la aplicación se instrumenta igual y no intenta exportar.
  /// </remarks>
  public string OtlpEndpoint { get; init; } = string.Empty;

  /// <summary>
  /// Proporción de peticiones muestreadas (0 a 1). En producción trazar el 100% del
  /// tráfico es caro y casi nunca hace falta.
  /// </summary>
  [Range(0d, 1d)]
  public double SamplingRatio { get; init; } = 1d;

  /// <summary>
  /// Endpoint válido y utilizable: comprueba que sea un URI absoluto, no solo que no esté
  /// vacío.
  /// </summary>
  /// <remarks>
  /// Sin esto, un espacio de más en una variable de entorno hacía que <c>new Uri(...)</c>
  /// lanzara en el arranque, y con <c>restart: unless-stopped</c> eso es un crash-loop por
  /// un endpoint de métricas.
  /// </remarks>
  public bool ExportsTraces
      => !string.IsNullOrWhiteSpace(OtlpEndpoint)
         && Uri.TryCreate(OtlpEndpoint, UriKind.Absolute, out _);
}
