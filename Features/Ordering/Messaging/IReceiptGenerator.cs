using ApiEcommerce.Features.Ordering.Events;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <summary>
/// Qué se hace cuando se coloca una orden: dibujar su comprobante, guardarlo y dejar la
/// orden apuntando a él. El <b>efecto</b>, separado del transporte.
/// </summary>
/// <remarks>
/// <para>
/// Es el gemelo de <c>IProductPurchasedHandler</c> y existe por la misma lección de
/// <c>planning/18</c>: <b>lo que vive dentro de un <c>BackgroundService</c> atado a AMQP no
/// se puede probar</b>. Con el efecto fuera, el test que de verdad importa —el que falla a
/// mitad y comprueba que el mensaje se reintenta— se escribe sin broker delante.
/// </para>
/// <para>
/// Y deja al consumidor siendo lo que debe ser: fontanería AMQP (ack, reintentos, DLQ) que
/// no sabe qué significa el mensaje que transporta.
/// </para>
/// </remarks>
public interface IReceiptGenerator
{
  /// <summary>
  /// Genera el comprobante de la orden del evento.
  /// </summary>
  /// <remarks>
  /// <b>Corre dentro de la transacción del inbox</b>, así que si lanza, la marca de
  /// «procesado» se deshace con él y el mensaje se reintenta.
  /// </remarks>
  /// <param name="event">El evento ya deserializado.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task HandleAsync(OrderPlaced @event, CancellationToken ct = default);
}
