using ApiEcommerce.Features.Ordering.Events;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <summary>
/// Qué se hace cuando se coloca una orden: dibujar su comprobante, guardarlo y dejar la
/// orden apuntando a él. El efecto, separado del transporte.
/// </summary>
/// <remarks>
/// Fuera del consumidor para poder probarlo sin broker delante, y para que el consumidor
/// se quede en fontanería AMQP que no sabe qué significa el mensaje.
/// </remarks>
public interface IReceiptGenerator
{
  /// <summary>Genera el comprobante de la orden del evento.</summary>
  /// <remarks>
  /// Corre dentro de la transacción del inbox: si lanza, la marca de «procesado» se deshace
  /// con él y el mensaje se reintenta.
  /// </remarks>
  /// <param name="event">El evento ya deserializado.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task HandleAsync(OrderPlaced @event, CancellationToken ct = default);
}
