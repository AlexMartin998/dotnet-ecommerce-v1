using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ApiEcommerce.Shared.Messaging;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>Publica en el exchange de eventos, esperando confirmación del broker.</summary>
/// <remarks>
/// El canal se reutiliza entre publicaciones y se recrea si se ha cerrado; abrir uno por mensaje
/// es un viaje de ida y vuelta al broker por evento. Los <c>IChannel</c> no prometen ser
/// thread-safe, así que el acceso se serializa con un semáforo.
/// </remarks>
public sealed class RabbitMqEventPublisher(
    RabbitMqConnection connection,
    IOptions<RabbitMqOptions> options) : IEventPublisher, IAsyncDisposable
{
  private readonly RabbitMqOptions _options = options.Value;

  private readonly SemaphoreSlim _gate = new(1, 1);

  private IChannel? _channel;

  public async Task PublishAsync(
      Guid messageId, string eventType, string payload, CancellationToken ct = default)
  {
    var properties = new BasicProperties
    {
      // MessageId es lo que usa el consumidor para deduplicar (ver ProcessedMessage).
      MessageId = messageId.ToString(),
      Type = eventType,
      ContentType = "application/json",
      // Persistente: el mensaje sobrevive a un reinicio del broker.
      DeliveryMode = DeliveryModes.Persistent,
      Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds())
    };

    var body = Encoding.UTF8.GetBytes(payload);

    await _gate.WaitAsync(ct);

    try
    {
      var channel = await GetOrOpenChannelAsync(ct);

      await channel.BasicPublishAsync(
          exchange: _options.Exchange,
          routingKey: eventType,
          mandatory: true,          // si ninguna cola encaja, el broker devuelve el mensaje
          basicProperties: properties,
          body: body,
          cancellationToken: ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Un fallo puede haber dejado el canal cerrado; guardarlo impediría reconectar.
      await DiscardChannelAsync();
      throw;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>Devuelve el canal vivo, abriendo uno nuevo si hace falta.</summary>
  private async Task<IChannel> GetOrOpenChannelAsync(CancellationToken ct)
  {
    if (_channel is { IsOpen: true }) return _channel;

    await DiscardChannelAsync();

    // Tipo propio: el outbox distingue "broker caído" (no cuenta intento) de "este mensaje falla".
    var conn = await connection.TryGetConnectionAsync(ct)
        ?? throw new BrokerUnavailableException("RabbitMQ is not available.");

    // Con confirms, BasicPublishAsync no vuelve hasta el ack del broker: sin ellos, el outbox
    // marcaría como enviado algo que el broker nunca recibió.
    _channel = await conn.CreateChannelAsync(
        new CreateChannelOptions(publisherConfirmationsEnabled: true,
                                 publisherConfirmationTrackingEnabled: true),
        cancellationToken: ct);

    return _channel;
  }

  private async Task DiscardChannelAsync()
  {
    if (_channel is null) return;

    try { await _channel.DisposeAsync(); }
    catch (Exception) { /* cerrar un canal ya roto no puede impedir abrir el siguiente */ }

    _channel = null;
  }

  public async ValueTask DisposeAsync()
  {
    await _gate.WaitAsync();

    try { await DiscardChannelAsync(); }
    finally { _gate.Release(); }

    _gate.Dispose();
  }
}
