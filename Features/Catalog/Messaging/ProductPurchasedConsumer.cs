using System.Text;
using System.Text.Json;
using ApiEcommerce.Features.Catalog.Events;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ApiEcommerce.Shared.Messaging;

namespace ApiEcommerce.Features.Catalog.Messaging;


/// <summary>
/// Consume <see cref="ProductPurchased"/> de la cola y reacciona (aquí: avisa de stock
/// bajo). En este repo el consumidor vive en el mismo proceso que el publicador para
/// que el ejemplo sea autocontenido; <b>el diseño no cambia</b> si mañana es otro
/// servicio: lo único compartido es el contrato del evento y el nombre de la cola.
/// </summary>
/// <remarks>
/// Demuestra las cuatro cosas que hay que hacer bien en un consumidor:
/// <list type="number">
/// <item><b>ack manual</b> — confirmar solo tras procesar de verdad;</item>
/// <item><b>idempotencia</b> — deduplicar por <c>MessageId</c>;</item>
/// <item><b>reintentos acotados</b> — y DLQ cuando se agotan;</item>
/// <item><b>prefetch</b> — no acaparar mensajes que otra réplica podría procesar.</item>
/// </list>
/// </remarks>
public sealed class ProductPurchasedConsumer(
    RabbitMqConnection connection,
    IServiceScopeFactory scopeFactory,
    IOptions<RabbitMqOptions> options,
    ILogger<ProductPurchasedConsumer> logger) : BackgroundService
{
  private readonly RabbitMqOptions _options = options.Value;

  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

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
        // SIN filtro que excluya OperationCanceledException: una OCE que NO venga del
        // stoppingToken (timeout de comando de SqlClient, timeout interno del cliente
        // AMQP) escaparía de ExecuteAsync, y desde .NET 6 el default es
        // BackgroundServiceExceptionBehavior.StopHost: un timeout en un job de fondo
        // tumbaría la API entera.
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

    // ⚠️ Con publisher confirms. Este canal no solo consume: `ScheduleRetryAsync` PUBLICA
    // por él. Sin confirms, `BasicPublishAsync` vuelve sin excepción aunque el mensaje no
    // haya llegado a ninguna cola (`mandatory: true` sin handler de retorno lo descarta en
    // silencio) y justo después se hacía ack del original: **pérdida silenciosa**. Medido:
    // borrando el binding del exchange de reintento, el mensaje se evaporaba sin un solo
    // log de error. El publicador del outbox ya usaba confirms; este camino no lo heredó.
    await using var channel = await conn.CreateChannelAsync(
        new CreateChannelOptions(publisherConfirmationsEnabled: true,
                                 publisherConfirmationTrackingEnabled: true),
        cancellationToken: ct);

    // QoS: como mucho N mensajes sin confirmar por consumidor. Sin esto, RabbitMQ
    // empuja la cola entera a la primera réplica que se conecte y las demás quedan
    // ociosas mientras esa se atraganta.
    await channel.BasicQosAsync(0, _options.PrefetchCount, global: false, cancellationToken: ct);

    // ⚠️ Este canal se comparte entre el consumo y la publicación del reintento, y eso es
    // seguro por una razón CONCRETA que conviene no olvidar: `ConsumerDispatchConcurrency`
    // vale 1 por defecto, así que con `prefetch = 10` las entregas se despachan de una en
    // una. Si alguien sube esa concurrencia, hay que darle a la publicación su propio
    // canal — los `IChannel` no prometen ser thread-safe.
    //
    // Sin lo de abajo, una excepción dentro del dispatcher del canal (p. ej. si el propio
    // ack falla porque el canal se cerró) se publica en este evento y, si nadie está
    // suscrito, se pierde: fallo completamente mudo.
    channel.CallbackExceptionAsync += (_, e) =>
    {
      logger.LogError(e.Exception, "RabbitMQ channel callback failed");
      return Task.CompletedTask;
    };

    var consumer = new AsyncEventingBasicConsumer(channel);
    consumer.ReceivedAsync += (_, args) => HandleAsync(channel, args, ct);

    // autoAck: false -> confirmamos NOSOTROS, después de procesar. Con autoAck true,
    // el mensaje se da por bueno al entregarlo y un fallo al procesarlo lo pierde.
    await channel.BasicConsumeAsync(
        _options.Queue, autoAck: false, consumer: consumer, cancellationToken: ct);

    logger.LogInformation("Consuming {Queue} (prefetch {Prefetch})",
        _options.Queue, _options.PrefetchCount);

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
      // Se comprueba el TIPO antes de deserializar. System.Text.Json sobre un record
      // posicional NO falla con un payload ajeno: rellena con default (0, null). Si
      // mañana el binding pasa a `product.*` —que es justo el motivo de usar un
      // exchange topic—, un `product.created` se convertiría en un ProductPurchased
      // con ceros y dispararía una alerta de stock falsa.
      if (eventType != ProductPurchased.EventType)
      {
        logger.LogError("Unexpected event type {EventType}; sending to DLQ", eventType);
        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        return;
      }

      var json = Encoding.UTF8.GetString(args.Body.Span);
      var @event = JsonSerializer.Deserialize<ProductPurchased>(json, SerializerOptions);

      if (@event is null || messageId == Guid.Empty)
      {
        // Mensaje ilegible: reencolarlo no lo va a arreglar nunca. Directo a la DLQ.
        logger.LogError("Unprocessable message {MessageId}; sending to DLQ", args.BasicProperties.MessageId);
        await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
        return;
      }

      using var scope = scopeFactory.CreateScope();
      var inbox = scope.ServiceProvider.GetRequiredService<IMessageInbox>();
      var handler = scope.ServiceProvider.GetRequiredService<IProductPurchasedHandler>();

      // ---- idempotencia + efecto, ATÓMICOS --------------------------------
      // El outbox garantiza at-least-once, así que este mensaje PUEDE llegar dos veces.
      // Quien impide el duplicado de verdad es la clave primaria de ProcessedMessages,
      // dentro de la MISMA transacción que el efecto. El porqué —un P0 real— está en
      // IMessageInbox, junto al mecanismo.
      //
      // Esto vivía aquí dentro, y por eso el arreglo del P0 se quedó sin test: no había
      // forma de hacer fallar el efecto sin un broker delante. Ahora el consumidor es
      // fontanería AMQP y la garantía es una pieza que se prueba sola.
      var processed = await inbox.ProcessOnceAsync(
          messageId, eventType!, token => handler.HandleAsync(@event, token), ct);

      if (!processed)
        logger.LogInformation("Duplicate {MessageId} ignored", messageId);

      await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
    }
    catch (JsonException ex)
    {
      // Reencolarlo no lo va a arreglar NUNCA. El comentario de arriba ya decía que un
      // mensaje ilegible va directo a la DLQ, pero solo cubría el payload literal `null`:
      // un cuerpo que no parsea lanza aquí, ANTES de aquella comprobación, y caía en el
      // catch genérico gastando los 3 intentos y dos TTL para acabar igual en la DLQ.
      logger.LogError(ex, "Unparseable payload for {MessageId}; sending to DLQ", messageId);
      await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
    }
    catch (Exception ex) when (IsConcurrentDuplicate(ex))
    {
      // SOLO el choque en la PK de ProcessedMessages: otra réplica ya lo procesó.
      // No es un error, es la deduplicación funcionando. Quién sabe reconocerlo es el
      // inbox, que es de quien es la tabla — aquí solo se decide qué hacer con el ack.
      logger.LogInformation("Concurrent duplicate {MessageId} ignored", messageId);
      await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
    }
    catch (Exception ex)
    {
      // Contador REAL de intentos. Antes se usaba `args.Redelivered`, que es una BANDERA
      // del broker y no un contador: se pone a true en cuanto el mensaje se entregó alguna
      // vez sin ack —incluido un reinicio del pod sin ningún fallo— así que eran 2
      // intentos como mucho y con 0 ms entre ellos, porque un requeue devuelve el mensaje
      // a la CABEZA de la cola. Después se leyó de `x-death`, que sí cuenta pero es del
      // broker; hoy lo escribimos nosotros (ver AttemptHeader).
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

      await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
    }
  }

  /// <summary>
  /// Manda el mensaje a la cola de espera y confirma el original.
  /// </summary>
  /// <remarks>
  /// <para>
  /// ⚠️ <b>Publicar primero, confirmar después.</b> Al revés, morir entremedias pierde el
  /// mensaje: ya estaría confirmado y aún no reencolado. En este orden, morir entremedias
  /// solo provoca una reentrega —el mensaje nunca se confirmó— y el consumidor la
  /// deduplica. Se prefiere duplicar a perder, que es la misma regla del outbox.
  /// </para>
  /// <para>
  /// Se copian las cabeceras y se <b>incrementa la nuestra</b>: es lo que hace que el
  /// contador se acumule entre vueltas en vez de empezar de cero cada vez.
  /// </para>
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
      // ⚠️ Al exchange POR DEFECTO y con la COLA como routing key, no al exchange de
      // reintento. Con un exchange de por medio, TODAS las colas de espera ligadas a él
      // reciben una copia — y desde que el nombre lleva el TTL dentro, las de plazos
      // anteriores siguen ahí y ligadas. Medido: un solo reintento aparecía en las tres
      // colas de espera a la vez, y cada una lo devolvía a la principal por su cuenta.
      // El inbox las deduplica, así que no se ejecuta de más, pero multiplica el tráfico
      // y hace ilegible lo que está pasando.
      //
      // Publicar a la cola concreta es además lo que de verdad se quiere decir: "este
      // mensaje, a esperar AQUÍ". El exchange nunca aportó enrutado: solo tenía un
      // binding.
      await channel.BasicPublishAsync(
          exchange: string.Empty,
          routingKey: _options.RetryQueue,
          mandatory: true,
          basicProperties: properties,
          body: args.Body.ToArray(),
          cancellationToken: ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Con confirms activos, un mensaje que no encuentra cola destino lanza aquí
      // (`312 NO_ROUTE`) en vez de perderse. Si no se puede encolar el reintento, el
      // mensaje va a la DLQ: es peor perderlo que dejarlo donde alguien pueda verlo.
      logger.LogError(ex, "Could not schedule the retry; sending to DLQ instead");
      await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
      return;
    }

    await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
  }

  /// <summary>
  /// ¿Es el choque de clave primaria del inbox? Se pregunta a través de un scope porque
  /// el filtro de un <c>catch</c> corre fuera del que abrió <c>HandleAsync</c>.
  /// </summary>
  private bool IsConcurrentDuplicate(Exception exception)
  {
    using var scope = scopeFactory.CreateScope();

    return scope.ServiceProvider.GetRequiredService<IMessageInbox>().IsConcurrentDuplicate(exception);
  }

}
