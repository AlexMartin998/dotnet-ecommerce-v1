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

  /// <summary>Las colas sobre las que se puede operar: es la allowlist.</summary>
  /// <remarks>
  /// Son las mismas suscripciones que declara <see cref="RabbitMqConnection"/>: una cola que este
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
      // Passive: pregunta por una cola que ya existe sin crearla ni tocar sus argumentos.
      var declared = await channel.QueueDeclarePassiveAsync(subscription.DeadLetterQueue, ct);

      status.Add(new DeadLetterStatus(
          subscription.Queue, subscription.DeadLetterQueue, declared.MessageCount));
    }

    return status;
  }

  public async Task<int> ReplayAsync(string queue, int max, CancellationToken ct = default)
  {
    // El nombre llega desde fuera: se resuelve contra las suscripciones registradas, la allowlist.
    var subscription = _subscriptions.FirstOrDefault(s =>
                           string.Equals(s.Queue, queue, StringComparison.Ordinal))
                       ?? throw new DeadLetterQueueNotFoundException(queue);

    var conn = await connection.TryGetConnectionAsync(ct)
        ?? throw new BrokerUnavailableException("RabbitMQ is not available.");

    // Con confirms: este canal publica, y sin ellos un mensaje no encolado se perdería tras el ack.
    await using var channel = await conn.CreateChannelAsync(
        new CreateChannelOptions(publisherConfirmationsEnabled: true,
                                 publisherConfirmationTrackingEnabled: true),
        cancellationToken: ct);

    var moved = 0;

    while (moved < max && !ct.IsCancellationRequested)
    {
      // BasicGet y no un consumidor: la operación tiene que terminar y decir cuántos movió.
      var message = await channel.BasicGetAsync(subscription.DeadLetterQueue, autoAck: false, ct);

      if (message is null) break;   // no queda nada

      var properties = new BasicProperties
      {
        MessageId = message.BasicProperties.MessageId,
        Type = message.BasicProperties.Type,
        ContentType = message.BasicProperties.ContentType,
        DeliveryMode = DeliveryModes.Persistent,
        // Presupuesto a cero: con los intentos gastados moriría en la primera entrega.
        Headers = RetryAttempts.With(message.BasicProperties.Headers, 0)
      };

      try
      {
        // Al exchange principal: el mensaje recorre el mismo camino que uno nuevo.
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
        // Se devuelve a la dead-letter y se para: es el sitio del que se recupera.
        logger.LogError(ex,
            "Could not replay a message from {Queue}; leaving it in the dead-letter queue",
            subscription.DeadLetterQueue);

        await channel.BasicNackAsync(message.DeliveryTag, multiple: false, requeue: true, CancellationToken.None);
        break;
      }

      // El ack va después del publish: al revés, morir entremedias pierde el mensaje.
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
