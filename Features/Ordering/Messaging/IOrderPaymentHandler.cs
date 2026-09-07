using ApiEcommerce.Features.Ordering.Events;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <summary>
/// Qué hace Ordering cuando le confirman un cobro: pasar la orden a pagada y anunciarlo.
/// </summary>
/// <remarks>
/// El efecto vive fuera del consumidor para poder probarlo sin broker. Es lo único que
/// puede mover una orden a <c>Paid</c>.
/// </remarks>
public interface IOrderPaymentHandler
{
  Task HandleAsync(PaymentCapturedNotice notice, CancellationToken ct = default);
}
