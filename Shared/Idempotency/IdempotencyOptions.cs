using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Plazos y límites de la idempotencia de peticiones, sección <c>Idempotency</c>.
/// </summary>
/// <remarks>
/// La retención de los comandos ya ejecutados no está aquí: la gobierna
/// <c>Outbox:RetentionDays</c>, y ese plazo debe cubrir el peor reintento de un cliente.
/// </remarks>
public sealed class IdempotencyOptions
{
  /// <summary>Nombre de la sección de configuración.</summary>
  public const string SectionName = "Idempotency";

  /// <summary>
  /// Segundos que vive el marcador de «petición en vuelo».
  /// </summary>
  /// <remarks>
  /// En el camino normal el marcador se suelta al terminar; este plazo es solo la red por
  /// si el proceso muere a mitad. Que caduque antes de tiempo no permite doble ejecución:
  /// la duplicada pasa la puerta y choca contra la clave primaria de <c>ExecutedCommands</c>.
  /// </remarks>
  [Range(5, 3600)]
  public int ReservationTtlSeconds { get; init; } = 60;

  /// <summary>
  /// Longitud máxima de la <c>Idempotency-Key</c> que manda el cliente.
  /// </summary>
  /// <remarks>
  /// 255 es lo que aceptan Stripe y el borrador de la IETF; la clave acaba dentro de la
  /// clave primaria de <c>ExecutedCommands</c>.
  /// </remarks>
  [Range(16, 1024)]
  public int MaxKeyLength { get; init; } = 255;

  /// <summary><see cref="ReservationTtlSeconds"/> como <see cref="TimeSpan"/>.</summary>
  public TimeSpan ReservationTtl => TimeSpan.FromSeconds(ReservationTtlSeconds);
}
