using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Http;


/// <summary>Límites de tasa (sección <c>RateLimit</c>).</summary>
/// <remarks>
/// Están en configuración para poder ajustarlos sin recompilar y para que los tests de
/// integración, que mandan decenas de peticiones desde una sola IP, no se limiten a sí
/// mismos. Los valores por defecto son los que había cuando eran constantes.
/// </remarks>
public sealed class RateLimitOptions
{
  public const string SectionName = "RateLimit";

  /// <summary>Peticiones permitidas por IP y ventana, para toda la API.</summary>
  [Range(1, 1_000_000)]
  public int GlobalPermitLimit { get; init; } = 100;

  /// <summary>Duración de la ventana global, en segundos.</summary>
  [Range(1, 3600)]
  public int GlobalWindowSeconds { get; init; } = 60;

  /// <summary>
  /// Peticiones permitidas en <c>/auth</c>. Es el límite que de verdad importa: sin él, el
  /// lockout de Identity se sortea probando contraseñas contra muchos usuarios distintos.
  /// </summary>
  [Range(1, 1_000_000)]
  public int AuthPermitLimit { get; init; } = 10;

  /// <summary>Duración de la ventana de <c>/auth</c>, en segundos.</summary>
  [Range(1, 3600)]
  public int AuthWindowSeconds { get; init; } = 60;

  /// <summary>Refrescos por IP en la ventana.</summary>
  /// <remarks>
  /// Aparte de <see cref="AuthPermitLimit"/>: refresh lo dispara la app en cada recarga, no una
  /// persona tecleando, y con el límite de login el front recibía 429 al recargar.
  /// </remarks>
  [Range(1, 1_000_000)]
  public int RefreshPermitLimit { get; init; } = 30;

  [Range(1, 3600)]
  public int RefreshWindowSeconds { get; init; } = 60;
}
