using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

namespace ApiEcommerce.Shared.Messaging.RabbitMq;


/// <summary>
/// Conexión compartida al broker y declaración de la topología (exchanges y colas).
/// </summary>
/// <remarks>
/// Una conexión TCP por proceso y muchos canales: el handshake AMQP es caro y el broker limita
/// las conexiones, pero los <c>IChannel</c> no son thread-safe, así que cada componente usa el
/// suyo. Es perezosa y reintentable, para que un broker caído no impida arrancar.
/// </remarks>
public sealed class RabbitMqConnection(
    IOptions<RabbitMqOptions> options,
    IEnumerable<EventSubscription> subscriptions,
    ILogger<RabbitMqConnection> logger) : IAsyncDisposable
{
  private readonly RabbitMqOptions _options = options.Value;

  /// <summary>Las colas a declarar, una por consumidor registrado.</summary>
  /// <remarks>
  /// Llegan por DI desde <c>AddEventConsumer</c>, o sea desde los slices: aquí no se nombra
  /// ninguna. Vacío es legítimo —una réplica que solo publica— y entonces solo hay exchange.
  /// </remarks>
  private readonly IReadOnlyList<EventSubscription> _subscriptions = [.. subscriptions];

  private readonly SemaphoreSlim _gate = new(1, 1);

  private IConnection? _connection;

  /// <summary>Devuelve la conexión, abriéndola si hace falta, o <c>null</c> si no hay broker.</summary>
  /// <remarks>
  /// El primer <c>if</c> es un fast-path deliberadamente fuera del semáforo, para no serializar
  /// cada publicación; es seguro porque el campo solo se asigna con la conexión lista del todo.
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
        // Reconexión automática: ante un corte, el cliente rehace conexión y canales.
        AutomaticRecoveryEnabled = true,
        TopologyRecoveryEnabled = true,
        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),

        // Explícito: el consumidor publica el reintento por el mismo canal por el que consume,
        // y eso solo es seguro con las entregas despachadas de una en una.
        ConsumerDispatchConcurrency = 1
      };

      // Se libera SIEMPRE la anterior: con recuperación automática, una huérfana revive con su
      // canal y su consumidor, y acabaríamos con dos consumidores sobre la misma cola.
      if (_connection is not null)
      {
        await _connection.DisposeAsync();
        _connection = null;
      }

      // Variable LOCAL: el campo se publica solo con la topología ya declarada, porque el
      // fast-path de arriba devolvería si no una conexión abierta pero sin exchange.
      var connection = await factory.CreateConnectionAsync(ct);

      try
      {
        await DeclareTopologyAsync(connection, ct);
      }
      catch
      {
        // Sin este dispose, una topología fallida deja la conexión viva y enganchada al broker:
        // con AutomaticRecoveryEnabled nadie la cierra, y el bucle del consumidor crea otra cada 10 s.
        await connection.DisposeAsync();
        throw;
      }

      _connection = connection;
      logger.LogInformation("Connected to RabbitMQ");

      return _connection;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // No se propaga: quien llama reintenta más tarde, y un broker caído no debe tumbar la API.
      // Se loguea solo el mensaje porque una caída es esperada y la traza inundaría el log.
      // Un 406 sí es permanente —una cola existe con otros argumentos— y no una caída del broker.
      if (ex is OperationInterruptedException { ShutdownReason.ReplyCode: 406 })
        logger.LogError(ex,
            "RabbitMQ topology mismatch (406): a queue already exists with different arguments. " +
            "Messaging is DOWN until it is fixed — delete the offending queue or change its name. " +
            "This is NOT a broker outage and retrying will not fix it.");
      else
        logger.LogWarning("RabbitMQ unavailable ({Error}); events stay in the outbox", ex.Message);

      return null;
    }
    finally
    {
      _gate.Release();
    }
  }

  /// <summary>
  /// Declara el exchange y, por cada suscripción, su cola con su dead-letter y su cola de espera.
  /// Es idempotente mientras los parámetros no cambien.
  /// </summary>
  /// <remarks>Todo <c>durable</c>: sin eso, reiniciar el broker pierde la cola y su contenido.</remarks>
  private async Task DeclareTopologyAsync(IConnection connection, CancellationToken ct)
  {
    await using var channel = await connection.CreateChannelAsync(cancellationToken: ct);

    // Exchange topic: un consumidor puede suscribirse a `product.*` sin que el publicador lo sepa.
    await channel.ExchangeDeclareAsync(
        _options.Exchange, ExchangeType.Topic, durable: true, autoDelete: false,
        cancellationToken: ct);

    foreach (var subscription in _subscriptions)
      await DeclareSubscriptionAsync(channel, subscription, ct);
  }

  /// <summary>Cola principal, dead-letter y cola de espera de una suscripción.</summary>
  private async Task DeclareSubscriptionAsync(
      IChannel channel, EventSubscription subscription, CancellationToken ct)
  {
    // Dead letter, una POR SUSCRIPCIÓN: es fanout, y dos colas sobre la misma repartirían cada
    // mensaje muerto a las dos dead-letters.
    await channel.ExchangeDeclareAsync(
        subscription.DeadLetterExchange, ExchangeType.Fanout, durable: true, autoDelete: false,
        cancellationToken: ct);

    await channel.QueueDeclareAsync(
        subscription.DeadLetterQueue, durable: true, exclusive: false, autoDelete: false,
        cancellationToken: ct);

    await channel.QueueBindAsync(
        subscription.DeadLetterQueue, subscription.DeadLetterExchange, routingKey: string.Empty,
        cancellationToken: ct);

    await channel.QueueDeclareAsync(
        subscription.Queue, durable: true, exclusive: false, autoDelete: false,
        arguments: new Dictionary<string, object?>
        {
          // Un nack sin requeue manda el mensaje aquí automáticamente.
          ["x-dead-letter-exchange"] = subscription.DeadLetterExchange
        },
        cancellationToken: ct);

    await channel.QueueBindAsync(
        subscription.Queue, _options.Exchange, subscription.RoutingKey, cancellationToken: ct);

    // ---- reintento con espera -------------------------------------------------
    // Cola de espera sin consumidor: el mensaje caduca por `x-message-ttl` y el broker lo
    // dead-letterea de vuelta al exchange principal.
    // Topología NUEVA en vez de cambiar la principal: redeclararla con otros argumentos da 406.
    // Sin ligarla a ningún exchange: si no, cada cola de espera recibiría copia de cada reintento.
    var retryQueue = subscription.RetryQueue(_options.RetryDelaySeconds);

    await channel.QueueDeclareAsync(
        retryQueue, durable: true, exclusive: false, autoDelete: false,
        arguments: new Dictionary<string, object?>
        {
          ["x-message-ttl"] = _options.RetryDelaySeconds * 1000,
          ["x-dead-letter-exchange"] = _options.Exchange,
          // Sin esto conservaría la routing key de entrada y no volvería a la cola principal.
          ["x-dead-letter-routing-key"] = subscription.RoutingKey
        },
        cancellationToken: ct);

    logger.LogInformation(
        "RabbitMQ topology ready: {Exchange} -[{RoutingKey}]-> {Queue} " +
        "(retry {Retry} every {Delay}s, dlq {Dlq})",
        _options.Exchange, subscription.RoutingKey, subscription.Queue, retryQueue,
        _options.RetryDelaySeconds, subscription.DeadLetterQueue);
  }

  public async ValueTask DisposeAsync()
  {
    if (_connection is not null) await _connection.DisposeAsync();
    _gate.Dispose();
  }
}
