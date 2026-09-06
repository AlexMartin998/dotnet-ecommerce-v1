using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Exceptions;

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
        NetworkRecoveryInterval = TimeSpan.FromSeconds(5),

        // Explícito y no heredado del default: `ProductPurchasedConsumer` publica el
        // reintento por el MISMO canal por el que consume, y eso solo es seguro porque
        // las entregas se despachan de una en una. Si esto sube, hay que darle a la
        // publicación su propio canal.
        ConsumerDispatchConcurrency = 1
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

      try
      {
        await DeclareTopologyAsync(connection, ct);
      }
      catch
      {
        // ⚠️ FUGA DE CONEXIONES. Sin este dispose, una topología que falla deja la
        // conexión local huérfana — y con AutomaticRecoveryEnabled sigue **viva y
        // enganchada al broker para siempre**, porque nadie tiene ya la referencia.
        // El bucle del consumidor crea otra cada 10 s. Medido: 22 fallos consecutivos =
        // 22 conexiones fugadas, exactamente 1:1, y ninguna se cerraba sola; a ese ritmo
        // son ~8.600 al día hasta agotar los descriptores del broker.
        await connection.DisposeAsync();
        throw;
      }

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
      // ⚠️ Un 406 PRECONDITION_FAILED no es "el broker está caído": es que una cola ya
      // existe con argumentos distintos a los que pedimos (el caso típico: cambiar
      // `RabbitMq:RetryDelaySeconds`, que fija el `x-message-ttl` de la cola de espera).
      // Es un error de CONFIGURACIÓN, permanente, y tratarlo como una caída dejaba la
      // mensajería entera abajo reintentando en bucle con un simple WARNING.
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

    // ---- reintento con espera -------------------------------------------------
    // Cola de ESPERA, sin consumidor. El mensaje entra, caduca por `x-message-ttl` y el
    // broker lo dead-letterea de vuelta al exchange principal, donde la cola principal lo
    // recoge otra vez. Cada vuelta el broker incrementa `x-death[].count`, que es el
    // contador real de intentos que antes no existía.
    //
    // ⚠️ Se añade como topología NUEVA en vez de cambiar el `x-dead-letter-exchange` de la
    // cola principal. Redeclarar una cola existente con argumentos distintos da
    // PRECONDITION_FAILED (406) y cierra el canal: habría que borrar la cola en producción
    // —con sus mensajes— para desplegar esto. Así el despliegue es aditivo y sin parada.
    await channel.ExchangeDeclareAsync(
        _options.RetryExchange, ExchangeType.Topic, durable: true, autoDelete: false,
        cancellationToken: ct);

    await channel.QueueDeclareAsync(
        _options.RetryQueue, durable: true, exclusive: false, autoDelete: false,
        arguments: new Dictionary<string, object?>
        {
          ["x-message-ttl"] = _options.RetryDelaySeconds * 1000,
          ["x-dead-letter-exchange"] = _options.Exchange,
          // Sin esto conservaría la routing key con la que entró aquí, y el mensaje no
          // encontraría la cola principal al volver.
          ["x-dead-letter-routing-key"] = _options.RoutingKey
        },
        cancellationToken: ct);

    await channel.QueueBindAsync(
        _options.RetryQueue, _options.RetryExchange, _options.RoutingKey, cancellationToken: ct);

    logger.LogInformation(
        "RabbitMQ topology ready: {Exchange} -> {Queue} (retry {Retry} every {Delay}s, dlq {Dlq})",
        _options.Exchange, _options.Queue, _options.RetryQueue, _options.RetryDelaySeconds,
        _options.DeadLetterQueue);
  }

  public async ValueTask DisposeAsync()
  {
    if (_connection is not null) await _connection.DisposeAsync();
    _gate.Dispose();
  }
}
