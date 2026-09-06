using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ApiEcommerce.Shared.Messaging;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>Publica en el exchange de eventos, esperando confirmación del broker.</summary>
public sealed class RabbitMqEventPublisher(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options) : IEventPublisher
{
  private readonly RabbitMqOptions _options = options.Value;

  public async Task PublishAsync(
      Guid messageId, string eventType, string payload, CancellationToken ct = default)
  {
    // Tipo propio y no InvalidOperationException: el outbox necesita distinguir
    // "broker caído" (no es culpa de este mensaje, no cuenta como intento) de
    // "este mensaje falla" (sí cuenta). Ver BrokerUnavailableException.
    var conn = await connection.TryGetConnectionAsync(ct)
        ?? throw new BrokerUnavailableException("RabbitMQ is not available.");

    // publisherConfirmations: BasicPublishAsync no vuelve hasta que el broker ha
    // confirmado (ack). Sin esto, "publicado" solo significa "escrito en un socket"
    // y el outbox marcaría como enviado algo que el broker nunca recibió.
    await using var channel = await conn.CreateChannelAsync(
        new CreateChannelOptions(publisherConfirmationsEnabled: true,
                                 publisherConfirmationTrackingEnabled: true),
        cancellationToken: ct);

    var properties = new BasicProperties
    {
      // MessageId es lo que usa el consumidor para deduplicar (ver ProcessedMessage).
      MessageId = messageId.ToString(),
      Type = eventType,
      ContentType = "application/json",
      // Persistente: el mensaje sobrevive a un reinicio del broker. Con colas durables
      // pero mensajes transitorios, la cola sobrevive vacía — que es lo peor de los dos.
      DeliveryMode = DeliveryModes.Persistent,
      Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    };

    await channel.BasicPublishAsync(
        exchange: _options.Exchange,
        routingKey: eventType,
        mandatory: true,          // si ninguna cola encaja, el broker devuelve el mensaje
        basicProperties: properties,
        body: Encoding.UTF8.GetBytes(payload),
        cancellationToken: ct);
  }
}
