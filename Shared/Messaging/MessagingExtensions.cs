using ApiEcommerce.Shared.Messaging.Consumers;
using ApiEcommerce.Shared.Messaging.RabbitMq;

namespace ApiEcommerce.Shared.Messaging;


public static class MessagingExtensions
{
  /// <summary>
  /// Outbox + RabbitMQ (publicador y consumidor).
  /// </summary>
  /// <remarks>
  /// <b>El outbox se registra siempre, el broker solo si está configurado.</b> Esa
  /// asimetría es deliberada: escribir el evento es parte de la transacción de
  /// negocio y no puede depender de que haya broker. Sin RabbitMQ, las compras se
  /// completan igual y los eventos se acumulan en la tabla hasta que vuelva.
  /// </remarks>
  public static IServiceCollection AddMessaging(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<RabbitMqOptions>()
        .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();   // configuración inválida = no arranca, no falla en la primera petición

    // Scoped: comparte el AppDbContext del request, que es justo lo que hace que el
    // evento se confirme en la misma transacción que el cambio de negocio.
    services.AddScoped<IEventOutbox, EventOutbox>();

    var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                  ?? new RabbitMqOptions();

    if (!options.IsEnabled) return services;

    services.AddSingleton<RabbitMqConnection>();
    services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

    services.AddHostedService<OutboxPublisher>();
    services.AddHostedService<ProductPurchasedConsumer>();

    return services;
  }
}
