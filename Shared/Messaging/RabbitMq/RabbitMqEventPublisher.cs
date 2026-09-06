using System.Text;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using ApiEcommerce.Shared.Messaging;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>Publica en el exchange de eventos, esperando confirmación del broker.</summary>
/// <remarks>
/// <para>
/// ⚠️ <b>El canal se reutiliza entre publicaciones.</b> Antes se abría y cerraba uno por
/// mensaje, y abrir un canal es un viaje de ida y vuelta al broker: con
/// <c>Outbox:BatchSize</c> en 50, eran 50 canales por cada vuelta del publicador, cada
/// uno con su negociación. Funcionaba, pero pagaba el precio de una conexión nueva por
/// evento.
/// </para>
/// <para>
/// ⚠️ Los <c>IChannel</c> <b>no prometen ser thread-safe</b>, así que el acceso se
/// serializa con un semáforo. No es un cuello de botella real: quien publica es un único
/// <c>BackgroundService</c> que drena el outbox en serie, y el semáforo solo está por si
/// mañana alguien inyecta este publicador en otro sitio — que es exactamente el tipo de
/// suposición que se rompe sola con el tiempo.
/// </para>
/// <para>
/// El canal se recrea si se ha cerrado (caída del broker, un 406, un <c>NO_ROUTE</c> que
/// tumbe el canal). Guardar uno muerto y publicar por él daría un error genérico en vez
/// de reconectar.
/// </para>
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
      // Persistente: el mensaje sobrevive a un reinicio del broker. Con colas durables
      // pero mensajes transitorios, la cola sobrevive vacía — que es lo peor de los dos.
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
      // Un fallo puede haber dejado el canal cerrado; si se guarda, la siguiente
      // publicación fallaría con un error de canal cerrado en vez de reconectar.
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

    // Tipo propio y no InvalidOperationException: el outbox necesita distinguir
    // "broker caído" (no es culpa de este mensaje, no cuenta como intento) de
    // "este mensaje falla" (sí cuenta). Ver BrokerUnavailableException.
    var conn = await connection.TryGetConnectionAsync(ct)
        ?? throw new BrokerUnavailableException("RabbitMQ is not available.");

    // publisherConfirmations: BasicPublishAsync no vuelve hasta que el broker ha
    // confirmado (ack). Sin esto, "publicado" solo significa "escrito en un socket"
    // y el outbox marcaría como enviado algo que el broker nunca recibió.
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
