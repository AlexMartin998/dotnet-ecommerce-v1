using ApiEcommerce.Features.Catalog.Events;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Catalog.Messaging;


/// <summary>
/// Consume <see cref="ProductPurchased"/> y dispara su efecto (aquí: el aviso de stock
/// bajo).
/// </summary>
/// <remarks>
/// <para>
/// Toda la fontanería AMQP —ack manual, deduplicación por <c>MessageId</c>, reintentos con
/// espera, DLQ, prefetch— vive en <see cref="EventConsumer{TConsumer,TEvent}"/>, con los
/// porqués de cada decisión. <b>Aquí solo queda lo que es del catálogo</b>: qué evento se
/// escucha y quién lo atiende.
/// </para>
/// <para>
/// Estaba todo en esta clase hasta que apareció el segundo consumidor
/// (<c>order.placed</c>, <c>planning/20</c>). Copiarla habría duplicado doscientas líneas
/// donde varias son arreglos de bugs medidos: el próximo arreglo entraría en una copia y
/// la otra se quedaría con el bug.
/// </para>
/// </remarks>
public sealed class ProductPurchasedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    EventSubscriptionOf<ProductPurchasedConsumer> subscription,
    ILogger<ProductPurchasedConsumer> logger)
    : EventConsumer<ProductPurchasedConsumer, ProductPurchased>(
        connection, scopeFactory, options, subscription, logger)
{
  /// <remarks>
  /// El handler se resuelve del scope del mensaje y no se inyecta: este consumidor es un
  /// singleton y el efecto es <c>Scoped</c> (comparte el <c>AppDbContext</c> con la
  /// transacción del inbox, que es justo lo que hace que la marca y el efecto se confirmen
  /// juntos).
  /// </remarks>
  protected override Task HandleAsync(
      IServiceProvider services, ProductPurchased @event, CancellationToken ct)
      => services.GetRequiredService<IProductPurchasedHandler>().HandleAsync(@event, ct);
}
