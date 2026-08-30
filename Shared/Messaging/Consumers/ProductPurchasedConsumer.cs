using System.Text;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Models;
using ApiEcommerce.Shared.Messaging.Events;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;

namespace ApiEcommerce.Shared.Messaging.Consumers;


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

    await using var channel = await conn.CreateChannelAsync(cancellationToken: ct);

    // QoS: como mucho N mensajes sin confirmar por consumidor. Sin esto, RabbitMQ
    // empuja la cola entera a la primera réplica que se conecte y las demás quedan
    // ociosas mientras esa se atraganta.
    await channel.BasicQosAsync(0, _options.PrefetchCount, global: false, cancellationToken: ct);

    // Sin esto, una excepción dentro del dispatcher del canal (p. ej. si el propio
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

      // ---- idempotencia ---------------------------------------------------
      // El outbox garantiza at-least-once, así que este mensaje PUEDE llegar dos
      // veces. La clave primaria de ProcessedMessages es la que lo impide de verdad:
      // si dos réplicas procesan el duplicado a la vez, una de las dos revienta al
      // insertar y ese es el resultado correcto.
      if (await db.ProcessedMessages.AnyAsync(m => m.Id == messageId, ct))
      {
        logger.LogInformation("Duplicate {MessageId} ignored", messageId);
        await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
        return;
      }

      // ⚠️ La marca se escribe ANTES de aplicar el efecto, no después.
      // El AnyAsync de arriba es solo un atajo barato: entre esa consulta y el INSERT
      // cabe otra réplica, así que si el efecto fuera primero se aplicaría DOS VECES
      // y la PK solo arbitraría cuál de las dos filas sobrevive. Con este orden, la
      // restricción única es lo que ABRE la puerta al efecto: quien pierde el choque
      // no llega a ejecutarlo.
      db.ProcessedMessages.Add(new ProcessedMessage { Id = messageId, Type = eventType! });
      await db.SaveChangesAsync(ct);

      await ProcessAsync(@event, ct);

      await channel.BasicAckAsync(args.DeliveryTag, multiple: false, CancellationToken.None);
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
      // ⚠️ LIMITACIÓN CONOCIDA. `redelivered` es una bandera del BROKER, no un
      // contador: se pone a true en cuanto el mensaje se entregó alguna vez sin ack,
      // incluido un reinicio del pod sin ningún fallo de proceso. Efectivamente esto
      // son 2 intentos como máximo y con 0 ms entre ellos (un requeue devuelve el
      // mensaje a la CABEZA de la cola, así que se reentrega de inmediato).
      //
      // Reintentos contados y con backoff exigen una RETRY QUEUE con `x-message-ttl`
      // que dead-letterea de vuelta a la principal, y leer `x-death[0].count`. Está
      // anotado en 06-estado-y-roadmap.md; mientras tanto se prefiere esto a fingir
      // un contador que no existe.
      var toDlq = args.Redelivered;

      logger.LogError(ex, "Failed to process {MessageId} (redelivered: {Redelivered}) -> {Action}",
          messageId, args.Redelivered, toDlq ? "DLQ" : "requeue");

      await channel.BasicNackAsync(args.DeliveryTag, multiple: false, requeue: !toDlq, CancellationToken.None);
    }
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
