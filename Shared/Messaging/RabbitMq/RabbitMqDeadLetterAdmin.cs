using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <inheritdoc cref="IDeadLetterAdmin"/>
public sealed class RabbitMqDeadLetterAdmin(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options,
    IEnumerable<EventSubscription> subscriptions,
    ILogger<RabbitMqDeadLetterAdmin> logger) : IDeadLetterAdmin
{
  private readonly RabbitMqOptions _options = options.Value;

  /// <summary>
  /// Las colas sobre las que se puede operar. <b>Es la allowlist.</b>
  /// </summary>
  /// <remarks>
  /// Son las mismas suscripciones que declara <see cref="RabbitMqConnection"/>, o sea las
  /// que registró cada slice. Un slice que se añade aparece aquí solo; una cola que este
  /// servicio no consume no es alcanzable desde el endpoint.
  /// </remarks>
  private readonly IReadOnlyList<EventSubscription> _subscriptions = [.. subscriptions];

  public async Task<IReadOnlyList<DeadLetterStatus>> GetStatusAsync(CancellationToken ct = default)
  {
    var conn = await connection.TryGetConnectionAsync(ct)
        ?? throw new BrokerUnavailableException("RabbitMQ is not available.");

    await using var channel = await conn.CreateChannelAsync(cancellationToken: ct);

    var status = new List<DeadLetterStatus>(_subscriptions.Count);

    foreach (var subscription in _subscriptions)
    {
      // Passive: pregunta por una cola que YA existe y devuelve su recuento, sin crearla
      // ni tocar sus argumentos. Declararla en activo aquí sería pedir un 406 el día que
      // alguien cambie el TTL, y peor: este método es de solo lectura y debe serlo.
      var declared = await channel.QueueDeclarePassiveAsync(subscription.DeadLetterQueue, ct);

      status.Add(new DeadLetterStatus(
          subscription.Queue, subscription.DeadLetterQueue, declared.MessageCount));
    }

    return status;
  }

  public async Task<int> ReplayAsync(string queue, int max, CancellationToken ct = default)
  {
    // ⚠️ El nombre llega desde fuera: se resuelve contra las suscripciones REGISTRADAS y no
    // se usa tal cual. Sin esto el endpoint movería mensajes de cualquier cola del broker,
    // que además es compartido con otros proyectos.
    var subscription = _subscriptions.FirstOrDefault(s =>
                           string.Equals(s.Queue, queue, StringComparison.Ordinal))
                       ?? throw new DeadLetterQueueNotFoundException(queue);

    var conn = await connection.TryGetConnectionAsync(ct)
        ?? throw new BrokerUnavailableException("RabbitMQ is not available.");

    // Con publisher confirms: este canal PUBLICA. Sin ellos, `BasicPublishAsync` vuelve sin
    // excepción aunque el mensaje no llegue a ninguna cola, y justo después haríamos ack en
    // la DLQ: pérdida silenciosa del único mensaje que quedaba.
    await using var channel = await conn.CreateChannelAsync(
        new CreateChannelOptions(publisherConfirmationsEnabled: true,
                                 publisherConfirmationTrackingEnabled: true),
        cancellationToken: ct);

    var moved = 0;

    while (moved < max && !ct.IsCancellationRequested)
    {
      // BasicGet y no un consumidor: la operación tiene que TERMINAR y quien la lanza
      // necesita saber cuántos movió. autoAck en false — confirmamos nosotros, y después
      // de publicar.
      var message = await channel.BasicGetAsync(subscription.DeadLetterQueue, autoAck: false, ct);

      if (message is null) break;   // no queda nada

      var properties = new BasicProperties
      {
        MessageId = message.BasicProperties.MessageId,
        Type = message.BasicProperties.Type,
        ContentType = message.BasicProperties.ContentType,
        DeliveryMode = DeliveryModes.Persistent,
        // ⚠️ Presupuesto A CERO. Si volviera con los intentos gastados, moriría en la
        // primera entrega y esta herramienta no recuperaría nada. Es la razón de que el
        // contador sea nuestro y no `x-death`, que sobrevive al paso por la DLQ.
        Headers = RetryAttempts.With(message.BasicProperties.Headers, 0)
      };

      try
      {
        // Al exchange principal con la routing key de la suscripción: el mensaje recorre
        // el MISMO camino que uno nuevo, en vez de colarse por la puerta de atrás.
        await channel.BasicPublishAsync(
            exchange: _options.Exchange,
            routingKey: subscription.RoutingKey,
            mandatory: true,
            basicProperties: properties,
            body: message.Body.ToArray(),
            cancellationToken: ct);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // No se pudo publicar: se devuelve el mensaje a la dead-letter y se para. Dejarlo
        // donde estaba es lo correcto — es el sitio del que se recupera.
        logger.LogError(ex,
            "Could not replay a message from {Queue}; leaving it in the dead-letter queue",
            subscription.DeadLetterQueue);

        await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        break;
      }

      // ⚠️ El ack va DESPUÉS del publish. Al revés, morir entremedias pierde el mensaje.
      await channel.BasicAckAsync(message.DeliveryTag, multiple: false, CancellationToken.None);

      moved++;
    }

    if (moved > 0)
      logger.LogWarning(
          "Replayed {Count} message(s) from {DeadLetterQueue} back into {Queue}",
          moved, subscription.DeadLetterQueue, subscription.Queue);

    return moved;
  }
}
