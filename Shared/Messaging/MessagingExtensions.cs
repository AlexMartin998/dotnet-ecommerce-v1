using ApiEcommerce.Shared.Messaging.RabbitMq;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>Registro de la mensajería: outbox, inbox y, si hay broker, RabbitMQ.</summary>
public static class MessagingExtensions
{
  /// <summary>Registra el outbox y, si hay broker configurado, el transporte RabbitMQ.</summary>
  /// <remarks>
  /// El outbox, el inbox y la purga se registran siempre: son garantías sobre la base de datos
  /// y no pueden depender de que haya broker. Sin RabbitMQ, los eventos se acumulan en la tabla.
  /// </remarks>
  public static IServiceCollection AddMessaging(
      this IServiceCollection services, IConfiguration configuration)
  {
    // El outbox tiene su propia sección: no depende de que haya broker ni de cuál sea.
    services.AddOptions<OutboxOptions>()
        .Bind(configuration.GetSection(OutboxOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddOptions<RabbitMqOptions>()
        .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();   // configuración inválida = no arranca, no falla en la primera petición

    // Scoped: comparte el AppDbContext del request, y con él la transacción de negocio.
    services.AddScoped<IEventOutbox, EventOutbox>();

    // El inbox también siempre: procesar una sola vez es una garantía sobre la base de datos.
    services.AddScoped<IMessageInbox, MessageInbox>();

    // La purga tampoco depende del broker: con RabbitMQ apagado, las tablas crecen más.
    services.AddHostedService<OutboxCleaner>();

    var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                  ?? new RabbitMqOptions();

    if (!options.IsEnabled)
    {
      // Null Object con el MISMO lifetime que el real: sin él, el controller de dead-letters
      // no se podría construir y saldría un 500 de DI en vez del 503 que describe lo que pasa.
      services.AddSingleton<IDeadLetterAdmin, NoDeadLetterAdmin>();

      return services;
    }

    services.AddSingleton<RabbitMqConnection>();
    services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();
    services.AddSingleton<IDeadLetterAdmin, RabbitMqDeadLetterAdmin>();

    services.AddHostedService<OutboxPublisher>();

    return services;
  }


  /// <summary>
  /// Registra un consumidor de eventos con su cola, y solo si hay broker configurado.
  /// </summary>
  /// <remarks>
  /// La suscripción la trae el slice, para que <c>Shared</c> no conozca a sus consumidores y no
  /// se invierta la dirección Web → Features → Shared. Se registra dos veces: como
  /// <see cref="EventSubscription"/> para la topología y como <see cref="EventSubscriptionOf{TConsumer}"/>
  /// para que cada consumidor pida la suya.
  /// </remarks>
  /// <typeparam name="TConsumer">El <c>BackgroundService</c> que consume.</typeparam>
  /// <param name="services">Contenedor.</param>
  /// <param name="configuration">Configuración, para decidir si hay broker.</param>
  /// <param name="subscription">Su cola, su routing key y su dead-letter.</param>
  public static IServiceCollection AddEventConsumer<TConsumer>(
      this IServiceCollection services, IConfiguration configuration,
      EventSubscription subscription)
      where TConsumer : class, IHostedService
  {
    ArgumentNullException.ThrowIfNull(subscription);

    var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                  ?? new RabbitMqOptions();

    // La suscripción se registra DENTRO: subirla haría que una réplica sin broker declarara
    // colas que nadie consume.
    if (!options.IsEnabled) return services;

    services.AddSingleton(subscription);
    services.AddSingleton(new EventSubscriptionOf<TConsumer>(subscription));
    services.AddHostedService<TConsumer>();

    return services;
  }
}
