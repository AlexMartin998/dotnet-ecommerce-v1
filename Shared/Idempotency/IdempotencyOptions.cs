using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Plazos y límites de la idempotencia de peticiones, sección <c>Idempotency</c>.
/// </summary>
/// <remarks>
/// <para>
/// Antes eran tres <c>private static readonly</c> dentro del filtro. Además de saltarse
/// la regla del proyecto —toda sección se enlaza a una clase tipada y validada—, tenía
/// una consecuencia concreta: <b>la caducidad de la reserva no se podía probar</b>. Con
/// 60 s clavados en el código, el único test posible tardaba un minuto, así que no
/// existía y el comportamiento del lease caducado no estaba fijado por nadie.
/// </para>
/// </remarks>
public sealed class IdempotencyOptions
{
  public const string SectionName = "Idempotency";

  /// <summary>
  /// Horas que se recuerda la RESPUESTA de una operación ya completada.
  /// </summary>
  [Range(1, 168)]
  public int ResponseTtlHours { get; init; } = 24;

  /// <summary>
  /// Segundos que vive la RESERVA mientras la operación está en curso.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Corto a propósito, y distinto del anterior: si el proceso muere entre la reserva y
  /// el guardado (deploy, OOM-kill), con un TTL de 24 h la clave quedaba bloqueada un día
  /// entero devolviendo 409 por una operación que <b>nunca llegó a ejecutarse</b>.
  /// </para>
  /// <para>
  /// ⚠️ Es un <b>lease sin renovación</b>: una operación que tarde más que esto libera su
  /// propia clave, y una petición duplicada que llegue después se ejecutará de verdad.
  /// El plazo debe quedar holgadamente por encima del peor caso de la acción más lenta
  /// que lleve <c>[Idempotent]</c>. Deuda anotada en <c>planning/16</c> §16.6.
  /// </para>
  /// </remarks>
  [Range(5, 3600)]
  public int ReservationTtlSeconds { get; init; } = 60;

  /// <summary>
  /// Longitud máxima de la <c>Idempotency-Key</c> que manda el cliente.
  /// </summary>
  /// <remarks>
  /// La clave la elige el cliente y acaba entera dentro de una clave de Redis que vive
  /// <see cref="ResponseTtlHours"/> horas, en una instancia compartida con otros
  /// proyectos. Sin límite se aceptaban claves de 7000 caracteres (medido). 255 es lo
  /// que aceptan Stripe y el borrador de idempotencia de la IETF.
  /// </remarks>
  [Range(16, 1024)]
  public int MaxKeyLength { get; init; } = 255;

  public TimeSpan ResponseTtl => TimeSpan.FromHours(ResponseTtlHours);

  public TimeSpan ReservationTtl => TimeSpan.FromSeconds(ReservationTtlSeconds);
}
