using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Features.Ordering.Events;


/// <summary>
/// Se publica cuando una orden quedó registrada y cobrada. Dispara la generación del
/// comprobante.
/// </summary>
/// <remarks>
/// Se emite por el outbox, dentro de la transacción de la compra: o hay orden y evento, o
/// no hay ninguno. Lleva solo la identificación de la orden, no su detalle, para que el
/// consumidor relea el estado confirmado y no haya dos fuentes de verdad.
/// </remarks>
public sealed record OrderPlaced(
    int OrderId,
    string OrderNumber,
    string BuyerUserId,
    decimal Total,
    string Currency,
    DateTime OccurredAt) : IDomainEvent
{
  public static string EventType => "order.placed";
}
