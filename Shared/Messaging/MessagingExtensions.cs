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
    // El outbox tiene su propia sección: no depende de que haya broker ni de cuál sea.
    services.AddOptions<OutboxOptions>()
        .Bind(configuration.GetSection(OutboxOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddOptions<RabbitMqOptions>()
        .Bind(configuration.GetSection(RabbitMqOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();   // configuración inválida = no arranca, no falla en la primera petición

    // Scoped: comparte el AppDbContext del request, que es justo lo que hace que el
    // evento se confirme en la misma transacción que el cambio de negocio.
    services.AddScoped<IEventOutbox, EventOutbox>();

    // El inbox también SIEMPRE, y por el mismo motivo que el outbox: procesar un mensaje
    // exactamente una vez es una garantía sobre la base de datos, no sobre el broker.
    // Registrarlo dentro del `if` de RabbitMQ ataría una pieza transaccional a que haya
    // transporte — y además dejaría sus tests dependiendo de que hubiera broker.
    services.AddScoped<IMessageInbox, MessageInbox>();

    // La purga tampoco depende del broker: las tablas crecen aunque no haya nadie
    // publicando, y con RabbitMQ apagado crecen MÁS. Va fuera del `if` de abajo.
    services.AddHostedService<OutboxCleaner>();

    var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                  ?? new RabbitMqOptions();

    if (!options.IsEnabled) return services;

    services.AddSingleton<RabbitMqConnection>();
    services.AddSingleton<IEventPublisher, RabbitMqEventPublisher>();

    services.AddHostedService<OutboxPublisher>();

    return services;
  }


  /// <summary>
  /// Registra un consumidor de eventos, <b>solo si hay broker configurado</b>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Es la costura para que cada slice registre SUS consumidores sin que
  /// <c>Shared/Messaging</c> tenga que conocerlos: aquí vive el mecanismo (la conexión,
  /// el outbox, el publicador) y en <c>Features/&lt;Contexto&gt;/</c> vive quién reacciona
  /// a qué. Si <c>ProductPurchasedConsumer</c> se registrara aquí, <c>Shared</c>
  /// dependería de <c>Features</c> y la dirección declarada
  /// <b>Web → Features → Shared</b> se invertiría.
  /// </para>
  /// <para>
  /// La condición de "hay broker" se evalúa en ESTE método y no en cada slice: es la
  /// misma decisión para todos, y duplicarla es garantizar que algún día un slice la
  /// comprueba distinto.
  /// </para>
  /// </remarks>
  public static IServiceCollection AddEventConsumer<TConsumer>(
      this IServiceCollection services, IConfiguration configuration)
      where TConsumer : class, IHostedService
  {
    var options = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                  ?? new RabbitMqOptions();

    if (options.IsEnabled) services.AddHostedService<TConsumer>();

    return services;
  }
}
