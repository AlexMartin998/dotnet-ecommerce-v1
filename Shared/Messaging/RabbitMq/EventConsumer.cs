using System.Text;
using System.Text.Json;
using ApiEcommerce.Shared.Messaging.Events;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// Toda la fontanería AMQP de un consumidor: conexión, ack manual, deduplicación, reintentos con
/// espera y DLQ. Lo único que pone la subclase es qué hacer con el evento.
/// </summary>
/// <remarks>
/// Se hereda porque es mecanismo puro y no toma ninguna decisión de negocio. Copiarlo por
/// consumidor dejaría el próximo arreglo aplicado en una sola de las copias.
/// </remarks>
/// <typeparam name="TConsumer">
/// La subclase. Se nombra a sí misma para pedir <b>su</b> suscripción
/// (<see cref="EventSubscriptionOf{TConsumer}"/>) y para que el log lleve su categoría.
/// </typeparam>
/// <typeparam name="TEvent">El evento que consume, con su <c>EventType</c> estático.</typeparam>
public abstract class EventConsumer<TConsumer, TEvent>(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    EventSubscriptionOf<TConsumer> subscription,
    ILogger<TConsumer> logger) : BackgroundService
    where TConsumer : EventConsumer<TConsumer, TEvent>
    where TEvent : class, IDomainEvent
{
  private readonly RabbitMqOptions _options = options.Value;

  /// <summary>
  /// La cola de ESTE consumidor. Cerrada por tipo: pedir <c>EventSubscription</c> a secas
  /// devolvería «la última registrada» en cuanto haya dos consumidores.
  /// </summary>
  private readonly EventSubscription _subscription = subscription.Value;

  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);


  /// <summary>Qué hacer con el evento. Es lo único que pone la subclase.</summary>
  /// <remarks>
  /// Corre dentro de la transacción del inbox: si lanza, la marca de «procesado» se deshace y el
  /// mensaje se reintenta, así que tiene que poder ejecutarse dos veces. Recibe el
  /// <see cref="IServiceProvider"/> del scope del mensaje porque el consumidor es un singleton.
  /// </remarks>
  /// <param name="services">Servicios del scope de este mensaje.</param>
  /// <param name="event">El evento ya deserializado.</param>
  /// <param name="ct">Token de cancelación.</param>
  protected abstract Task HandleAsync(
      IServiceProvider services, TEvent @event, CancellationToken ct);


  /// <summary>
  /// Se llama cuando el mensaje ha agotado sus reintentos y va a la DLQ. Por defecto no hace nada.
  /// </summary>
  /// <remarks>
  /// Es el único momento en que «ya no habrá más intentos» es cierto: desde el <c>catch</c> de cada
  /// intento no se distingue «todavía no» de «no va a pasar». Corre en su propio scope y su propia
  /// transacción, y no debe lanzar, o el mensaje no llegaría a la DLQ.
  /// </remarks>
  /// <param name="services">Servicios de un scope nuevo.</param>
  /// <param name="event">El evento que no se pudo procesar.</param>
  /// <param name="ct">Token de cancelación.</param>
  protected virtual Task OnExhaustedAsync(
      IServiceProvider services, TEvent @event, CancellationToken ct) => Task.CompletedTask;


  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_options.IsEnabled)
    {
      logger.LogInformation("RabbitMq:ConnectionString is empty; consumer disabled");
      return;
    }

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await ConsumeAsync(stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;   // apagado ordenado
      }
      catch (Exception ex)
      {
        // Sin filtrar OperationCanceledException: una OCE ajena al stoppingToken tumbaría la API (StopHost).
        logger.LogError(ex, "Consumer loop failed; reconnecting in 10s");
      }

      try { await Task.Delay(TimeSpan.FromSeconds(10), stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }

  private async Task ConsumeAsync(CancellationToken ct)
  {
    var conn = await connection.TryGetConnectionAsync(ct);
    if (conn is null) return;   // broker caído: se reintenta en la siguiente vuelta

    // Con confirms: este canal también publica el reintento, y sin ellos se perdería en silencio.
    await using var channel = await conn.CreateChannelAsync(
        new CreateChannelOptions(publisherConfirmationsEnabled: true,
                                 publisherConfirmationTrackingEnabled: true),
        cancellationToken: ct);

    // QoS: como mucho N mensajes sin confirmar, para que una réplica no acapare la cola entera.
    await channel.BasicQosAsync(0, _options.PrefetchCount, global: false, cancellationToken: ct);

    // Compartir canal entre consumo y publicación solo es seguro con ConsumerDispatchConcurrency = 1.
    // Sin este handler, una excepción del dispatcher (un ack sobre un canal cerrado) se perdería.
    channel.CallbackExceptionAsync += (_, e) =>
    {
      logger.LogError(e.Exception, "RabbitMQ channel callback failed");
      return Task.CompletedTask;
    };

    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.ReceivedAsync += (_, args) => HandleAsync(channel, args, ct);

    // autoAck: false — confirmamos nosotros, después de procesar.
    await channel.BasicConsumeAsync(
        _subscription.Queue, autoAck: false, consumer: consumer, cancellationToken: ct);

    logger.LogInformation("Consuming {Queue} (prefetch {Prefetch})",
        _subscription.Queue, _options.PrefetchCount);

    // El consumidor vive mientras el canal esté abierto y no se cancele.
    while (!ct.IsCancellationRequested && channel.IsOpen)
      await Task.Delay(TimeSpan.FromSeconds(1), ct);
  }

  private async Task HandleAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken ct)
  {
    var messageId = Guid.TryParse(args.BasicProperties.MessageId, out var id) ? id : Guid.Empty;

    var eventType = args.BasicProperties.Type;

    try
    {
      // El tipo se comprueba antes de deserializar: un payload ajeno se rellenaría con default.
      if (eventType != TEvent.EventType)
      {
        logger.LogError("Unexpected event type {EventType}; sending to DLQ", eventType);
        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        return;
      }

      var json = Encoding.UTF8.GetString(args.Body.Span);
      var @event = JsonSerializer.Deserialize<TEvent>(json, SerializerOptions);

      if (@event is null || messageId == Guid.Empty)
      {
        // Mensaje ilegible: reencolarlo no lo va a arreglar nunca. Directo a la DLQ.
        logger.LogError("Unprocessable message {MessageId}; sending to DLQ", args.BasicProperties.MessageId);
        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        return;
      }

      using var scope = scopeFactory.CreateScope();
      var inbox = scope.ServiceProvider.GetRequiredService<IMessageInbox>();
      
      // El duplicado lo impide la PK de ProcessedMessages, en la misma transacción que el efecto.
      var processed = await inbox.ProcessOnceAsync(
          messageId, eventType!, token => HandleAsync(scope.ServiceProvider, @event, token), ct);

      if (!processed)
        logger.LogInformation("Duplicate {MessageId} ignored", messageId);

      await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
    }
    catch (JsonException ex)
    {
      // Reencolar no arregla un cuerpo que no parsea: directo a la DLQ, sin gastar intentos.
      logger.LogError(ex, "Unparseable payload for {MessageId}; sending to DLQ", messageId);
      await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
    }
    catch (Exception ex) when (IsConcurrentDuplicate(ex))
    {
      // Solo el choque en la PK de ProcessedMessages: otra réplica ya lo procesó, no es un error.
      logger.LogInformation("Concurrent duplicate {MessageId} ignored", messageId);
      await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
    }
    catch (Exception ex)
    {
      // Contador propio y no `args.Redelivered`, que es una bandera del broker y no un contador.
      var attempts = RetryAttempts.Read(args.BasicProperties.Headers) + 1;

      if (attempts < _options.MaxDeliveryAttempts)
      {
        logger.LogWarning(ex,
            "Failed to process {MessageId} (attempt {Attempt}/{Max}); retrying in {Delay}s",
            messageId, attempts, _options.MaxDeliveryAttempts, _options.RetryDelaySeconds);

        await ScheduleRetryAsync(channel, args, attempts, ct);
        return;
      }

      logger.LogError(ex,
          "Failed to process {MessageId} after {Attempts} attempt(s) -> DLQ",
          messageId, attempts);

      // Antes del nack: después, el mensaje ya está en la DLQ y morir aquí dejaría el fallo sin registrar.
      await NotifyExhaustedAsync(args, ct);

      await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
    }
  }

  /// <summary>Avisa al slice de que el mensaje se agotó, sin dejar que eso rompa el nack.</summary>
  /// <remarks>
  /// Todo el método va en <c>try/catch</c>: si el aviso se propagara, el mensaje no llegaría a la
  /// DLQ. Perder el registro del fallo es malo; perder el mensaje es peor.
  /// </remarks>
  private async Task NotifyExhaustedAsync(BasicDeliverEventArgs args, CancellationToken ct)
  {
    try
    {
      var @event = JsonSerializer.Deserialize<TEvent>(
          Encoding.UTF8.GetString(args.Body.Span), SerializerOptions);

      if (@event is null) return;

      using var scope = scopeFactory.CreateScope();

      await OnExhaustedAsync(scope.ServiceProvider, @event, ct);
    }
    catch (Exception ex)
    {
      logger.LogError(ex, "The exhausted-message hook failed; sending to DLQ anyway");
    }
  }

  /// <summary>
  /// Manda el mensaje a la cola de espera y confirma el original.
  /// </summary>
  /// <remarks>
  /// Publicar primero y confirmar después: al revés, morir entremedias pierde el mensaje; en este
  /// orden solo provoca una reentrega, que el inbox deduplica. Se copian las cabeceras y se
  /// incrementa la nuestra, que es lo que acumula el contador entre vueltas.
  /// </remarks>
  private async Task ScheduleRetryAsync(
      IChannel channel, BasicDeliverEventArgs args, int attempts, CancellationToken ct)
  {
    var properties = new BasicProperties
    {
      MessageId = args.BasicProperties.MessageId,
      Type = args.BasicProperties.Type,
      ContentType = args.BasicProperties.ContentType,
      DeliveryMode = DeliveryModes.Persistent,
      Headers = RetryAttempts.With(args.BasicProperties.Headers, attempts)
    };

    try
    {
      // Al exchange por defecto y con la COLA como routing key: con un exchange de por medio,
      // cada cola de espera ligada a él recibiría su copia del reintento.
      await channel.BasicPublishAsync(
          exchange: string.Empty,
          routingKey: _subscription.RetryQueue(_options.RetryDelaySeconds),
          mandatory: true,
          basicProperties: properties,
          body: args.Body.ToArray(),
          cancellationToken: ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Con confirms, un mensaje que no encuentra cola destino lanza (312 NO_ROUTE) en vez de perderse.
      logger.LogError(ex, "Could not schedule the retry; sending to DLQ instead");
      await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
      return;
    }

    await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
  }

  /// <summary>
  /// ¿Es el choque de clave primaria del inbox? Se pregunta a través de un scope nuevo porque el
  /// filtro de un <c>catch</c> corre fuera del que abrió <c>HandleAsync</c>.
  /// </summary>
  private bool IsConcurrentDuplicate(Exception exception)
  {
    using var scope = scopeFactory.CreateScope();

    return scope.ServiceProvider.GetRequiredService<IMessageInbox>().IsConcurrentDuplicate(exception);
  }
}
