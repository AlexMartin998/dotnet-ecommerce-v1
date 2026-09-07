using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <summary>Consume <c>payment.captured</c> y pasa la orden a pagada.</summary>
/// <remarks>
/// Es el consumidor que cruza contextos: Payments publica, Ordering reacciona, y lo único
/// que comparten es la routing key. Cola propia con su dead-letter propia.
/// </remarks>
public sealed class PaymentCapturedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    EventSubscriptionOf<PaymentCapturedConsumer> subscription,
    ILogger<PaymentCapturedConsumer> logger)
    : EventConsumer<PaymentCapturedConsumer, PaymentCapturedNotice>(
        connection, scopeFactory, options, subscription, logger)
{
  public static EventSubscription Subscription { get; } =
      EventSubscription.For("apiecommerce.order-payment", PaymentCapturedNotice.EventType);

  protected override Task HandleAsync(
      IServiceProvider services, PaymentCapturedNotice @event, CancellationToken ct)
      => services.GetRequiredService<IOrderPaymentHandler>().HandleAsync(@event, ct);
}
