using ApiEcommerce.Features.Catalog.Events;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Catalog.Messaging;


/// <summary>
/// Consume <see cref="ProductPurchased"/> y dispara su efecto.
/// </summary>
/// <remarks>
/// La fontanería AMQP (ack, deduplicación, reintentos, DLQ, prefetch) vive en
/// <see cref="EventConsumer{TConsumer,TEvent}"/>; aquí solo queda lo del catálogo.
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
  /// <summary>Resuelve el handler en el scope del mensaje y le pasa el evento.</summary>
  /// <remarks>
  /// El handler no se inyecta porque este consumidor es singleton y él es scoped:
  /// así comparte el <c>AppDbContext</c> con la transacción del inbox.
  /// </remarks>
  protected override Task HandleAsync(
      IServiceProvider services, ProductPurchased @event, CancellationToken ct)
      => services.GetRequiredService<IProductPurchasedHandler>().HandleAsync(@event, ct);
}
