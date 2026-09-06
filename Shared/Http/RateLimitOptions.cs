using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Http;


/// <summary>Límites de tasa (sección <c>RateLimit</c>).</summary>
/// <remarks>
/// Estaban como constantes en el código. Pasan a configuración por dos razones
/// concretas, no por gusto:
/// <list type="bullet">
/// <item>una API detrás de un gateway, o con un cliente móvil que hace ráfagas, necesita
/// otro número <b>sin recompilar</b>;</item>
/// <item>y los tests de integración y de concurrencia mandan decenas de peticiones desde
/// una sola IP: con el límite fijo se limitan a sí mismos y fallan con 429 por un motivo
/// que no tiene nada que ver con lo que prueban.</item>
/// </list>
/// Los valores por defecto son exactamente los que había antes, así que no cambia nada
/// para quien no configure la sección.
/// </remarks>
public sealed class RateLimitOptions
{
  public const string SectionName = "RateLimit";

  /// <summary>Peticiones permitidas por IP y ventana, para toda la API.</summary>
  [Range(1, 1_000_000)]
  public int GlobalPermitLimit { get; init; } = 100;

  [Range(1, 3600)]
  public int GlobalWindowSeconds { get; init; } = 60;

  /// <summary>
  /// Peticiones permitidas en <c>/auth</c>. Es el límite que de verdad importa: sin él,
  /// el lockout de Identity se sortea probando contraseñas contra <b>muchos</b> usuarios
  /// distintos (password spraying).
  /// </summary>
  [Range(1, 1_000_000)]
  public int AuthPermitLimit { get; init; } = 10;

  [Range(1, 3600)]
  public int AuthWindowSeconds { get; init; } = 60;
}
