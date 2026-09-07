using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Features.Ordering.Events;


/// <summary>La orden quedó pagada. Es lo que dispara el comprobante.</summary>
/// <remarks>
/// Antes lo disparaba <c>order.placed</c>, y eso emitía el comprobante de una compra que
/// nadie había pagado todavía.
/// </remarks>
public sealed record OrderPaid(
    int OrderId, string Number, string BuyerUserId, decimal Total, string Currency,
    DateTime PaidAt) : IDomainEvent
{
  public static string EventType => "order.paid";
}
