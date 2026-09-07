using ApiEcommerce.Shared.Messaging.Events;

namespace ApiEcommerce.Features.Payments.Events;


/// <summary>La pasarela confirmó el cobro de una orden.</summary>
/// <remarks>
/// Es lo único que autoriza a dar una orden por pagada. Vive en Payments porque lo emite
/// Payments; quien reacciona es Ordering, con su propio consumidor.
/// </remarks>
public sealed record PaymentCaptured(
    int PaymentId,
    string Reference,
    int OrderId,
    string OrderNumber,
    string BuyerUserId,
    decimal Amount,
    string Currency,
    DateTime CapturedAt) : IDomainEvent
{
  public static string EventType => "payment.captured";
}
