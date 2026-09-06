using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <summary>Consume <see cref="OrderPlaced"/> y genera el comprobante de la orden.</summary>
/// <remarks>
/// El PDF se dibuja aquí, con su propio presupuesto de reintentos, para que un fallo
/// generándolo no invalide la compra. La fontanería AMQP está en
/// <see cref="EventConsumer{TConsumer,TEvent}"/>.
/// </remarks>
public sealed class OrderPlacedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    EventSubscriptionOf<OrderPlacedConsumer> subscription,
    ILogger<OrderPlacedConsumer> logger)
    : EventConsumer<OrderPlacedConsumer, OrderPlaced>(
        connection, scopeFactory, options, subscription, logger)
{
  /// <summary>La cola de este consumidor, con su dead-letter propia.</summary>
  /// <remarks>
  /// El nombre lo declara el slice y no la configuración compartida, y
  /// <see cref="EventSubscription.For"/> le da una DLX propia: con la <c>fanout</c> del
  /// catálogo, un comprobante muerto aparecería también en la DLQ de las compras.
  /// </remarks>
  public static EventSubscription Subscription { get; } =
      EventSubscription.For("apiecommerce.order-placed", OrderPlaced.EventType);

  protected override Task HandleAsync(
      IServiceProvider services, OrderPlaced @event, CancellationToken ct)
      => services.GetRequiredService<IReceiptGenerator>().HandleAsync(@event, ct);

  /// <summary>Marca la orden como comprobante fallido cuando se agotan los reintentos.</summary>
  /// <remarks>
  /// Es el único punto en que «ya no habrá más intentos» es cierto; sin él la orden se
  /// quedaría en <c>pending</c> y el cliente haría polling eterno sobre un PDF muerto en la
  /// DLQ.
  /// </remarks>
  protected override Task OnExhaustedAsync(
      IServiceProvider services, OrderPlaced @event, CancellationToken ct)
      => services.GetRequiredService<IOrderRepository>().SetReceiptFailedAsync(@event.OrderId, ct);
}
