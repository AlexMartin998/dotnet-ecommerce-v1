using System.Data;
using ApiEcommerce.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using ApiEcommerce.Shared.Db;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Drena la tabla outbox hacia el broker.
/// </summary>
/// <remarks>
/// <para>
/// Es un <see cref="BackgroundService"/> y no parte del request: publicar dentro de la
/// petición volvería a atar la latencia (y la disponibilidad) de la compra a la del
/// broker, que es justo lo que el outbox venía a desacoplar.
/// </para>
/// <para>
/// <b>Orden de las operaciones.</b> Primero se publica, luego se marca como procesado.
/// Al revés se perderían mensajes si el proceso muere entremedias. Así, como mucho, se
/// publica dos veces — <b>at-least-once</b>, y por eso el consumidor deduplica.
/// </para>
/// <para>
/// <b>Qué cuenta como intento.</b> Solo el fallo atribuible a UN mensaje. Un broker
/// caído no gasta intentos: si lo hiciera, una caída de segundos enterraría eventos
/// válidos que nadie volvería a publicar. Ver <see cref="BrokerUnavailableException"/>.
/// </para>
/// <para>
/// <b>Una sola réplica drena a la vez</b>, gracias a un <c>sp_getapplock</c> exclusivo.
/// Ver <see cref="TryAcquirePublisherLockAsync"/>.
/// </para>
/// </remarks>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IOptions<OutboxOptions> options,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
  private readonly OutboxOptions _options = options.Value;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var interval = TimeSpan.FromSeconds(_options.PublishIntervalSeconds);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await PublishPendingAsync(stoppingToken);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Un fallo aquí NO puede matar el BackgroundService: si el bucle termina,
        // nadie vuelve a publicar hasta que se reinicie el proceso.
        logger.LogError(ex, "Outbox publish loop failed; retrying in {Interval}", interval);
      }

      try { await Task.Delay(interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }

  private async Task PublishPendingAsync(CancellationToken ct)
  {
    // Scope propio: un BackgroundService es singleton y no puede inyectar un
    // AppDbContext (scoped) por constructor.
    using var scope = scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var transactions = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();

    await transactions.ExecuteAsync(async token =>
    {
      // Si otra réplica está drenando, esta vuelta no hace nada. Es lo correcto: el
      // trabajo no se pierde, se hace en la siguiente pasada (o la termina la otra).
      if (!await TryAcquirePublisherLockAsync(db, token))
      {
        logger.LogDebug("Another replica is draining the outbox; skipping this cycle");
        return 0;
      }

      return await DrainAsync(db, token);
    }, ct);
  }

  private async Task<int> DrainAsync(AppDbContext db, CancellationToken ct)
  {
    var maxAttempts = _options.MaxPublishAttempts;

    var pending = await db.OutboxMessages
        .Where(m => m.ProcessedAt == null && m.Attempts < maxAttempts)
        .OrderBy(m => m.Sequence)   // orden de INSERCIÓN, no de reloj: ver OutboxMessage.Sequence
        .Take(_options.BatchSize)
        .ToListAsync(ct);

    if (pending.Count == 0) return 0;

    var published = 0;

    foreach (var message in pending)
    {
      try
      {
        await publisher.PublishAsync(message.Id, message.Type, message.Payload, ct);

        message.ProcessedAt = DateTime.Now;
        message.LastError = null;
        published++;

        logger.LogInformation("Published {EventType} {MessageId}", message.Type, message.Id);
      }
      catch (BrokerUnavailableException ex)
      {
        // ⚠️ EL BROKER CAÍDO NO CONSUME INTENTOS. No es un fallo de ESTE mensaje: le
        // pasa igual a todos, y contarlo aquí hacía que una caída corta enterrara
        // eventos válidos para siempre. Medido: con 5 intentos cada 5 s, bastaban
        // **25 segundos** de broker caído —menos que el start_period de su propio
        // contenedor— para que el mensaje quedara con Attempts=5, fuera del filtro de
        // arriba y por tanto sin republicarse NUNCA, ni al volver el broker.
        //
        // Se corta la tanda (los siguientes fallarían igual) pero sin tocar Attempts:
        // el mensaje sigue vivo y se reintenta en la vuelta siguiente, tantas vueltas
        // como dure la caída. Lo ya publicado en esta tanda SÍ se guarda.
        logger.LogWarning(
            "Broker unavailable ({Error}); {Pending} event(s) stay in the outbox, no attempt consumed",
            ex.Message, pending.Count - published);
        break;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Aquí sí: el fallo es atribuible al mensaje (payload ilegible, sin cola
        // destino con `mandatory: true`…). Estos son los que de verdad hay que dejar
        // de reintentar, y solo estos.
        message.Attempts++;
        message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;

        // Traza completa solo cuando el mensaje se agota: los intentos intermedios
        // son ruido esperado.
        if (message.Attempts >= maxAttempts)
          logger.LogError(ex,
              "Giving up on {MessageId} after {Attempts} attempts; it stays in the outbox for manual review",
              message.Id, message.Attempts);
        else
          logger.LogWarning(
              "Failed to publish {MessageId} (attempt {Attempts}/{Max}): {Error}",
              message.Id, message.Attempts, maxAttempts, ex.Message);

        // `continue`, no `break`: el broker está vivo, así que un mensaje envenenado
        // no debe bloquear la cabecera de la tanda y retrasar a los que sí saldrían
        // (head-of-line blocking).
        continue;
      }
    }

    await db.SaveChangesAsync(ct);

    return published;
  }

  /// <summary>
  /// Toma un bloqueo aplicativo exclusivo para que <b>solo una réplica drene a la vez</b>.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Sin esto, con dos réplicas ambas leen el mismo lote y publican los mismos mensajes:
  /// no corrompe nada —el consumidor deduplica por <c>MessageId</c>— pero dobla el
  /// tráfico del broker y del consumidor.
  /// </para>
  /// <para>
  /// <b>Por qué un bloqueo global y no un claim por filas</b> (una columna
  /// <c>LockedUntil</c> con <c>UPDATE ... OUTPUT</c>): el claim permite que dos réplicas
  /// drenen <i>en paralelo</i>, y eso destruye justamente la garantía de orden que da
  /// <c>Sequence</c>. Además obliga a gestionar la expiración del claim para que una
  /// réplica que muere no deje filas bloqueadas. Aquí drenar es un trabajo de fondo cada
  /// pocos segundos y con lote acotado: serializarlo no cuesta nada y sale más simple y
  /// más correcto.
  /// </para>
  /// <para>
  /// ⚠️ <c>@LockOwner = 'Transaction'</c>: el bloqueo se suelta solo al hacer commit o
  /// rollback, incluso si el proceso muere. Con <c>'Session'</c> quedaría atado a una
  /// conexión del <i>pool</i>, que se reutiliza para otra cosa: la receta para un
  /// bloqueo que no suelta nadie.
  /// </para>
  /// </remarks>
  private async Task<bool> TryAcquirePublisherLockAsync(AppDbContext db, CancellationToken ct)
  {
    var transaction = db.Database.CurrentTransaction
        ?? throw new InvalidOperationException("The publisher lock requires an active transaction.");

    await using var command = db.Database.GetDbConnection().CreateCommand();

    command.Transaction = transaction.GetDbTransaction();
    command.CommandType = CommandType.StoredProcedure;
    command.CommandText = "sp_getapplock";

    command.Parameters.Add(new SqlParameter("@Resource", _options.PublisherLockName));
    command.Parameters.Add(new SqlParameter("@LockMode", "Exclusive"));
    command.Parameters.Add(new SqlParameter("@LockOwner", "Transaction"));
    // Timeout 0: no esperamos. Si otro lo tiene, se salta la vuelta y se reintenta en la
    // siguiente; encolar réplicas esperando un bloqueo solo acumula latencia.
    command.Parameters.Add(new SqlParameter("@LockTimeout", 0));

    var result = new SqlParameter { ParameterName = "@Result", SqlDbType = SqlDbType.Int, Direction = ParameterDirection.ReturnValue };
    command.Parameters.Add(result);

    await command.ExecuteNonQueryAsync(ct);

    // >= 0 concedido (0 inmediato, 1 tras esperar); < 0 no concedido.
    return result.Value is int code && code >= 0;
  }
}
