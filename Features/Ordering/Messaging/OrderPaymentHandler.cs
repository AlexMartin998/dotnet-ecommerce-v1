using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Messaging;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <inheritdoc cref="IOrderPaymentHandler"/>
public sealed class OrderPaymentHandler(
    IOrderRepository orders,
    IEventOutbox outbox,
    ILogger<OrderPaymentHandler> logger) : IOrderPaymentHandler
{
  public async Task HandleAsync(PaymentCapturedNotice notice, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(notice);

    // El estado se relee de la base: el mensaje solo identifica la orden.
    var order = await orders.FindWithItemsAsync(notice.OrderId, ct);

    if (order is null)
    {
      // No se lanza: reintentar no va a hacer aparecer una orden borrada.
      logger.LogWarning(
          "Payment {Reference} refers to order {OrderId}, which no longer exists",
          notice.Reference, notice.OrderId);
      return;
    }

    // Solo Placed -> Paid. Que la transición sea condicional es lo que la hace idempotente
    // ante un reenvío, y lo que impide resucitar una orden ya cancelada por abandono.
    if (!await orders.TryMarkPaidAsync(order.Id, ct))
    {
      logger.LogInformation(
          "Order {Number} was not awaiting payment ({Status}); nothing to do",
          order.Number, order.Status);
      return;
    }

    // En la MISMA transacción que la transición: el comprobante cuelga de este evento, y
    // publicarlo aparte dejaría órdenes pagadas sin comprobante y sin reintento.
    await outbox.EnqueueAsync(new OrderPaid(
        order.Id, order.Number, order.BuyerUserId, order.Total, order.Currency, DateTime.Now), ct);

    logger.LogInformation(
        "Order {Number} is paid ({Reference})", order.Number, notice.Reference);
  }
}
