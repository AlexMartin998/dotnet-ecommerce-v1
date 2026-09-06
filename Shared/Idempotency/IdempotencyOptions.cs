using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Plazos y límites de la idempotencia de peticiones, sección <c>Idempotency</c>.
/// </summary>
/// <remarks>
/// <para>
/// Antes eran <c>private static readonly</c> dentro del filtro. Además de saltarse la
/// regla del proyecto —toda sección se enlaza a una clase tipada y validada—, tenía una
/// consecuencia concreta: <b>la caducidad del marcador no se podía probar</b>. Con 60 s
/// clavados en el código, el único test posible tardaba un minuto, así que no existía.
/// </para>
/// <para>
/// La retención de los comandos ya ejecutados <b>no</b> está aquí: la gobierna
/// <c>Outbox:RetentionDays</c>, junto al resto de tablas que purga el recolector.
/// ⚠️ Ese plazo tiene que cubrir el PEOR reintento de un cliente: a partir de ahí, la
/// misma clave vuelve a ejecutar de verdad. (Stripe recuerda 24 h; Adyen, 7-14 días.)
/// </para>
/// </remarks>
public sealed class IdempotencyOptions
{
  public const string SectionName = "Idempotency";

  /// <summary>
  /// Segundos que vive el marcador de «petición en vuelo».
  /// </summary>
  /// <remarks>
  /// <para>
  /// En el camino normal el marcador se suelta al terminar la petición; este plazo es
  /// solo la red por si el proceso muere a mitad (deploy, OOM-kill). Sin él, la clave
  /// quedaría cerrada devolviendo 409 para siempre.
  /// </para>
  /// <para>
  /// ⚠️ Que caduque antes de tiempo ya <b>no</b> permite una doble ejecución: lo único
  /// que pasa es que una petición duplicada pasa la puerta y va a chocar contra la clave
  /// primaria de <c>ExecutedCommands</c>. Cuando este mismo plazo gobernaba la garantía,
  /// era un <i>lease</i> sin renovación y sí abría esa ventana.
  /// </para>
  /// </remarks>
  [Range(5, 3600)]
  public int ReservationTtlSeconds { get; init; } = 60;

  /// <summary>
  /// Longitud máxima de la <c>Idempotency-Key</c> que manda el cliente.
  /// </summary>
  /// <remarks>
  /// La clave la elige el cliente y acaba dentro de una clave de Redis y de la clave
  /// primaria de <c>ExecutedCommands</c>. Sin límite se aceptaban claves de 7000
  /// caracteres (medido). 255 es lo que aceptan Stripe y el borrador de la IETF.
  /// </remarks>
  [Range(16, 1024)]
  public int MaxKeyLength { get; init; } = 255;

  public TimeSpan ReservationTtl => TimeSpan.FromSeconds(ReservationTtlSeconds);
}
