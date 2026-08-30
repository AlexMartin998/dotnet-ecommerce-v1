using Microsoft.Extensions.Options;
using RabbitMQ.Client;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// Conexión compartida al broker y declaración de la topología (exchanges y colas).
/// </summary>
/// <remarks>
/// <para>
/// <b>Una conexión TCP por proceso, muchos canales.</b> Abrir una conexión por
/// mensaje es el error clásico con RabbitMQ: el handshake AMQP es caro y el broker
/// tiene un límite de conexiones. Los <c>IChannel</c>, en cambio, son baratos —pero
/// <b>no son thread-safe</b>, así que cada componente (publicador, consumidor) usa
/// el suyo.
/// </para>
/// <para>
/// La conexión es <b>perezosa y reintentable</b>: si el broker no está arriba al
/// arrancar, la app no cae. Es lo que permite que la API siga aceptando compras con
/// RabbitMQ caído, acumulando los eventos en el outbox.
/// </para>
/// </remarks>
public sealed class RabbitMqConnection(
    IOptions<RabbitMqOptions> options,
    ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
  private readonly RabbitMqOptions _options = options.Value;
  private readonly SemaphoreSlim _gate = new(1, 1);

  private IConnection? _connection;

  /// <remarks>
  /// El primer <c>if</c> es un fast-path deliberadamente <b>fuera</b> del semáforo:
  /// serializar cada publicación detrás de un lock sería un cuello de botella. Es
  /// seguro porque el campo solo se publica cuando la conexión está lista del todo
  /// (ver el cuerpo del <c>try</c>).
  /// </remarks>
  public async Task<IConnection?> TryGetConnectionAsync(CancellationToken ct)
  {
    if (!_options.IsEnabled) return null;
    if (_connection is { IsOpen: true }) return _connection;

    await _gate.WaitAsync(ct);
    try
    {
      if (_connection is { IsOpen: true }) return _connection;

      var factory = new ConnectionFactory
      {
        Uri = new Uri(_options.ConnectionString),
        // Reconexión automática: ante un corte, el cliente rehace conexión y canales
        // sin que haya que reiniciar el servicio.
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(5)
      };

      // La conexión anterior se libera SIEMPRE antes de crear otra. Si se
      // sobrescribiera el campo sin liberarla, con AutomaticRecoveryEnabled la vieja
      // seguiría viva con su temporizador de recuperación: al volver el broker se
      // recuperaría sola, TopologyRecoveryEnabled le devolvería su canal y su
      // consumidor, y acabaríamos con DOS conexiones y DOS consumidores sobre la
      // misma cola.
      if (_connection is not null)
      {
        await _connection.DisposeAsync();
        _connection = null;
      }

      // Variable LOCAL, no el campo: el campo se publica solo cuando la topología
      // está declarada. Si se asignara antes, el fast-path de arriba (que está fuera
      // del semáforo a propósito, para no serializar cada publicación) devolvería
      // una conexión abierta pero SIN exchange, y el publicador fallaría con un 404
      // de canal. Y si DeclareTopologyAsync fallara, el campo quedaría asignado para
      // siempre y la topología no se declararía nunca más.
      var connection = await factory.CreateConnectionAsync(ct);

      await DeclareTopologyAsync(connection, ct);

      _connection = connection;
      logger.LogInformation("Connected to RabbitMQ");

      return _connection;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // No se propaga: quien llama decide reintentar más tarde. Un broker caído no
      // debe tumbar la API.
      //
      // Se loguea el MENSAJE y no la excepción entera: un broker caído es una
      // condición ESPERADA y recuperable, y el bucle reintenta cada pocos segundos.
      // Volcar la traza completa cada vez inunda el log justo cuando hace falta
      // leerlo, y deja un incidente real sepultado.
      logger.LogWarning("RabbitMQ unavailable ({Error}); events stay in the outbox", ex.Message);
      return null;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Declara exchange, cola, dead-letter y binding. Es idempotente: declarar algo que
  /// ya existe con los mismos parámetros no hace nada.
  /// </summary>
  /// <remarks>
  /// Todo <c>durable</c> y los mensajes persistentes: sin eso, reiniciar el broker
  /// pierde la cola y su contenido.
  /// </remarks>
  private async Task DeclareTopologyAsync(IConnection connection, CancellationToken ct)
  {
    await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

    // Exchange de eventos: `topic` para que un consumidor pueda suscribirse a
    // `product.*` sin que el publicador sepa quién escucha.
    await channel.ExchangeDeclareAsync(
        _options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
        cancellationToken: ct);

    // Dead letter: donde acaban los mensajes que agotaron sus reintentos. Sin DLQ,
    // un mensaje envenenado se reencola para siempre y bloquea la cola.
    await channel.ExchangeDeclareAsync(
        _options.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
        cancellationToken: ct);

    await channel.QueueDeclareAsync(
        _options.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
        cancellationToken: ct);

    await channel.QueueBindAsync(
        _options.DeadLetterQueue, _options.DeadLetterExchange, routingKey: string.Empty,
        cancellationToken: ct);

    await channel.QueueDeclareAsync(
        _options.Queue, durable: true, exclusive: false, autoDelete: false,
        arguments: new Dictionary<string, object?>
        {
          // Un nack sin requeue manda el mensaje aquí automáticamente.
          ["x-dead-letter-exchange"] = _options.DeadLetterExchange
        },
        cancellationToken: ct);

    await channel.QueueBindAsync(
        _options.Queue, _options.Exchange, _options.RoutingKey, cancellationToken: ct);

    logger.LogInformation(
        "RabbitMQ topology ready: {Exchange} -> {Queue} (dlq {Dlq})",
        _options.Exchange, _options.Queue, _options.DeadLetterQueue);
  }

  public async ValueTask DisposeAsync()
  {
    if (_connection is not null) await _connection.DisposeAsync();
    _gate.Dispose();
  }
}
