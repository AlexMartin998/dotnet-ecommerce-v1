using System.Text;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Features.Catalog.Events;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using ApiEcommerce.Shared.Db;
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

  /// <summary>Umbral para el aviso de stock bajo.</summary>
  private const int LowStockThreshold = 5;

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
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      var transactions = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();

      // ---- idempotencia + efecto, ATÓMICOS --------------------------------
      // El outbox garantiza at-least-once, así que este mensaje PUEDE llegar dos veces.
      // La clave primaria de ProcessedMessages es lo que impide el duplicado de verdad:
      // si dos réplicas procesan el mismo mensaje a la vez, una revienta al insertar.
      //
      // ⚠️ La marca y el efecto van en la MISMA transacción, y eso corrige un bug real.
      // Antes la marca se confirmaba ANTES del efecto, con el razonamiento de que así la
      // restricción única "abría la puerta" al efecto y quien perdía el choque no lo
      // ejecutaba. Ese razonamiento valía cuando NO había reintentos; en cuanto los hubo,
      // se volvió al revés: si el efecto fallaba, la marca ya estaba confirmada, y en la
      // reentrega el mensaje se reconocía como duplicado, se hacía ack y **desaparecía sin
      // haberse procesado nunca**. Toda la maquinaria de reintentos era inerte para el
      // único caso para el que existe.
      //
      // En una sola transacción se cumplen las dos cosas: si el efecto falla, la marca se
      // deshace con él y el reintento puede volver a intentarlo; y si dos réplicas corren
      // a la vez, la PK hace fallar a una y su efecto se deshace también.
      var processed = await transactions.ExecuteAsync(async token =>
      {
        // Atajo barato para el caso normal (ya procesado): evita abrir el efecto.
        if (await db.ProcessedMessages.AnyAsync(m => m.Id == messageId, token))
          return false;

        db.ProcessedMessages.Add(new ProcessedMessage { Id = messageId, Type = eventType! });

        await ProcessAsync(@event, token);

        // El choque de PK sale AQUÍ, y arrastra al efecto en el rollback.
        await db.SaveChangesAsync(token);

        return true;
      }, ct);

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
    catch (DbUpdateException ex) when (ex.InnerException is SqlException { Number: 2601 or 2627 })
    {
      // SOLO el choque en la PK de ProcessedMessages: otra réplica ya lo procesó.
      // No es un error, es la deduplicación funcionando.
      //
      // El filtro por número de error es imprescindible: un `catch (DbUpdateException)`
      // a secas se tragaría también timeouts y deadlocks (1205), haría ack, y el
      // mensaje desaparecería de la cola SIN procesarse y con un log que dice
      // "duplicado ignorado". Justo la pérdida que la mensajería viene a evitar.
      logger.LogInformation("Concurrent duplicate {MessageId} ignored", messageId);
      await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
    }
    catch (Exception ex)
    {
      // Contador REAL de intentos, leído de `x-death`. Antes se usaba
      // `args.Redelivered`, que es una BANDERA del broker y no un contador: se pone a
      // true en cuanto el mensaje se entregó alguna vez sin ack —incluido un reinicio
      // del pod sin ningún fallo— así que eran 2 intentos como mucho y con 0 ms entre
      // ellos, porque un requeue devuelve el mensaje a la CABEZA de la cola.
      var attempts = DeliveryAttempts(args) + 1;

      if (attempts < _options.MaxDeliveryAttempts)
      {
        logger.LogWarning(ex,
            "Failed to process {MessageId} (attempt {Attempt}/{Max}); retrying in {Delay}s",
            messageId, attempts, _options.MaxDeliveryAttempts, _options.RetryDelaySeconds);

        await ScheduleRetryAsync(channel, args, ct);
        return;
      }

      logger.LogError(ex,
          "Failed to process {MessageId} after {Attempts} attempt(s) -> DLQ",
          messageId, attempts);

      await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: false, CancellationToken.None);
    }
  }

  /// <summary>
  /// Cuántas veces ha caducado ya este mensaje en la cola de reintento.
  /// </summary>
  /// <remarks>
  /// El broker escribe una entrada en <c>x-death</c> por cada cola desde la que se hizo
  /// dead-letter, con un <c>count</c> acumulado. Se busca la de la cola de reintento: las
  /// entradas de otras colas (la principal, cuando algo va a la DLQ) contarían otra cosa.
  /// </remarks>
  private long DeliveryAttempts(BasicDeliverEventArgs args)
  {
    if (args.BasicProperties.Headers?.TryGetValue("x-death", out var raw) is not true
        || raw is not IEnumerable<object> deaths)
      return 0;

    foreach (var death in deaths.OfType<IDictionary<string, object?>>())
    {
      // Los valores de texto viajan como byte[] en el cliente AMQP: comparar contra un
      // string sin convertir devuelve siempre false, en silencio.
      if (death.TryGetValue("queue", out var queue)
          && AsString(queue) == _options.RetryQueue
          && death.TryGetValue("count", out var count)
          && count is long value)
        return value;
    }

    return 0;
  }

  private static string? AsString(object? value) => value switch
  {
    byte[] bytes => Encoding.UTF8.GetString(bytes),
    string text => text,
    _ => value?.ToString()
  };

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
  /// Se copian las cabeceras <b>incluida <c>x-death</c></b>: es lo que hace que el contador
  /// se acumule entre vueltas en vez de empezar de cero cada vez.
  /// </para>
  /// </remarks>
  private async Task ScheduleRetryAsync(IChannel channel, BasicDeliverEventArgs args, CancellationToken ct)
  {
    var properties = new BasicProperties
    {
      MessageId = args.BasicProperties.MessageId,
      Type = args.BasicProperties.Type,
      ContentType = args.BasicProperties.ContentType,
      DeliveryMode = DeliveryModes.Persistent,
      Headers = args.BasicProperties.Headers
    };

    try
    {
      await channel.BasicPublishAsync(
          exchange: _options.RetryExchange,
          routingKey: _options.RoutingKey,
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
  /// El efecto de negocio. Aquí es un log de stock bajo; en un sistema real sería
  /// notificar a compras, escribir una proyección de lectura o llamar a un webhook.
  /// </summary>
  private Task ProcessAsync(ProductPurchased @event, CancellationToken ct)
  {
    logger.LogInformation(
        "Purchase processed: {Quantity} x {Sku} ({ProductName}), remaining {RemainingStock}",
        @event.Quantity, @event.Sku, @event.ProductName, @event.RemainingStock);

    if (@event.RemainingStock <= LowStockThreshold)
      logger.LogWarning(
          "LOW STOCK for {Sku}: only {RemainingStock} left", @event.Sku, @event.RemainingStock);

    return Task.CompletedTask;
  }
}
