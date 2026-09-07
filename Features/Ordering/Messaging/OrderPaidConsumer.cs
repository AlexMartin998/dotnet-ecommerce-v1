using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <summary>Consume <see cref="OrderPaid"/> y genera el comprobante.</summary>
/// <remarks>
/// Antes colgaba de <c>order.placed</c>, y eso emitía el comprobante de una compra que
/// nadie había pagado. El PDF tiene su propio presupuesto de reintentos para que un fallo
/// dibujándolo no invalide el cobro.
/// </remarks>
public sealed class OrderPaidConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    EventSubscriptionOf<OrderPaidConsumer> subscription,
    ILogger<OrderPaidConsumer> logger)
    : EventConsumer<OrderPaidConsumer, OrderPaid>(
        connection, scopeFactory, options, subscription, logger)
{
  public static EventSubscription Subscription { get; } =
      EventSubscription.For("apiecommerce.order-paid", OrderPaid.EventType);

  protected override Task HandleAsync(
      IServiceProvider services, OrderPaid @event, CancellationToken ct)
      => services.GetRequiredService<IReceiptGenerator>().HandleAsync(@event, ct);

  /// <summary>Marca el comprobante como fallido cuando se agotan los reintentos.</summary>
  /// <remarks>
  /// Es el único punto en que «ya no habrá más intentos» es cierto; sin él la orden se
  /// quedaría en <c>pending</c> y el cliente haría polling eterno sobre un PDF muerto.
  /// </remarks>
  protected override Task OnExhaustedAsync(
      IServiceProvider services, OrderPaid @event, CancellationToken ct)
      => services.GetRequiredService<IOrderRepository>().SetReceiptFailedAsync(@event.OrderId, ct);
}
