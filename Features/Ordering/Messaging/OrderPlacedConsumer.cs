using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Repository;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Messaging;


/// <summary>
/// Consume <see cref="OrderPlaced"/> y genera el comprobante de la orden.
/// </summary>
/// <remarks>
/// <para>
/// <b>Esta clase es la razón de que el PDF sea asíncrono.</b> La compra emite el evento
/// dentro de su transacción y responde; el documento se dibuja aquí, en otro hilo y con
/// su propio presupuesto de reintentos. Si el generador falla —o si nadie lo está
/// ejecutando— la compra sigue siendo válida: lo único que pasa es que el comprobante
/// tarda.
/// </para>
/// <para>
/// La fontanería AMQP está en <see cref="EventConsumer{TConsumer,TEvent}"/>. Aquí solo
/// queda qué evento se escucha y quién lo atiende.
/// </para>
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
  /// El nombre lo declara el <b>slice</b>, no la configuración compartida: dice a qué
  /// reacciona <c>Ordering</c>, así que es suyo. <see cref="EventSubscription.For"/> le da
  /// una DLX propia — con la <c>fanout</c> heredada del catálogo, un comprobante que
  /// muriera aparecería también en la DLQ de las compras.
  /// </remarks>
  public static EventSubscription Subscription { get; } =
      EventSubscription.For("apiecommerce.order-placed", OrderPlaced.EventType);

  protected override Task HandleAsync(
      IServiceProvider services, OrderPlaced @event, CancellationToken ct)
      => services.GetRequiredService<IReceiptGenerator>().HandleAsync(@event, ct);

  /// <summary>
  /// El comprobante ya no se va a generar: la orden lo dice.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ <b>Sin esto, `ReceiptStatus.Failed` era inalcanzable</b> —lo destapó una revisión—
  /// y la consecuencia era concreta: una orden cuyo PDF muriera en la DLQ se quedaba en
  /// <c>pending</c> <b>para siempre</b>, así que `GET /{id}/receipt` devolvía 409
  /// <c>receipt_not_ready</c> indefinidamente. Ese código significa «vuelve en un momento»,
  /// o sea que el cliente haría polling eterno sobre un documento que no va a existir.
  /// </para>
  /// <para>
  /// Marcarlo desde el <c>catch</c> de cada intento habría sido mentir mientras aún quedan
  /// reintentos. Este es el único punto en que «ya no habrá más» es cierto.
  /// </para>
  /// </remarks>
  protected override Task OnExhaustedAsync(
      IServiceProvider services, OrderPlaced @event, CancellationToken ct)
      => services.GetRequiredService<IOrderRepository>().SetReceiptFailedAsync(@event.OrderId, ct);
}
