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
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;   // apagado ordenado
      }
      catch (Exception ex)
      {
        // ⚠️ SIN filtro que excluya OperationCanceledException. Una OCE que NO venga del
        // stoppingToken —la cancelación de un SqlCommand, un timeout interno del cliente
        // AMQP, un token enlazado como el de abajo— se escapaba de este catch, salía de
        // ExecuteAsync y, como desde .NET 6 el default es StopHost, **tumbaba la API
        // entera**. Verificado reproduciendo la forma del bucle en un host mínimo.
        // Es el mismo error que el consumidor ya tenía corregido y este no.
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

    // ⚠️ PRESUPUESTO PARA LA TANDA. La transacción —y con ella el sp_getapplock— se
    // mantiene abierta durante todo el diálogo con el broker. En estado sano son ~0,3 s,
    // pero si la conexión está abierta y el broker no responde, cada publicación espera
    // hasta el ContinuationTimeout (20 s) y una tanda de 50 podía tener la transacción
    // abierta ~16 minutos: `log_reuse_wait_desc = ACTIVE_TRANSACTION` y el resto de
    // réplicas saltándose la vuelta. Se acota a dos intervalos.
    using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
    budget.CancelAfter(TimeSpan.FromSeconds(_options.PublishIntervalSeconds * 2));

    // ⚠️ REENTRANCIA. `ExecuteAsync` corre sobre la execution strategy de EF, que ante un
    // fallo transitorio (deadlock, timeout) **reejecuta el delegado entero** — incluidas
    // las publicaciones ya hechas al broker, que no se pueden deshacer. Es exactamente la
    // regla que este repo enuncia para `[Transactional]`, aquí incumplida. Este conjunto
    // vive FUERA del delegado y sobrevive al reintento, así que un replay no republica.
    var alreadyPublished = new HashSet<Guid>();

    try
    {
      await transactions.ExecuteAsync(async token =>
      {
        // Si otra réplica está drenando, esta vuelta no hace nada. Es lo correcto: el
        // trabajo no se pierde, se hace en la siguiente pasada (o la termina la otra).
        if (!await TryAcquirePublisherLockAsync(db, token))
        {
          logger.LogDebug("Another replica is draining the outbox; skipping this cycle");
          return 0;
        }

        return await DrainAsync(db, alreadyPublished, token);
      }, budget.Token);
    }
    catch (OperationCanceledException) when (!ct.IsCancellationRequested)
    {
      // Se agotó el presupuesto, no el apagado. Se suelta la transacción y el bloqueo, y
      // se reintenta en la vuelta siguiente: lo publicado ya está marcado.
      logger.LogWarning(
          "Outbox drain exceeded its {Seconds}s budget and was aborted; retrying next cycle",
          _options.PublishIntervalSeconds * 2);
    }
  }

  private async Task<int> DrainAsync(
      AppDbContext db, HashSet<Guid> alreadyPublished, CancellationToken ct)
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
        // Si la estrategia de EF reejecutó el delegado, esto ya salió al broker en el
        // intento anterior. Se marca sin volver a publicar.
        if (!alreadyPublished.Contains(message.Id))
        {
          await publisher.PublishAsync(message.Id, message.Type, message.Payload, ct);
          alreadyPublished.Add(message.Id);
        }

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
  /// <c>LockedUntil</c> con <c>UPDATE ... OUTPUT</c>): el claim obliga a gestionar la
  /// expiración para que una réplica que muere no deje filas bloqueadas para siempre, y
  /// añade dos columnas y una consulta con <c>UPDLOCK</c>/<c>READPAST</c> a cambio de un
  /// paralelismo que aquí no hace falta — drenar es un trabajo de fondo cada pocos
  /// segundos con lote acotado. Serializarlo sale más simple y no cuesta nada.
  /// <br/>
  /// ⚠️ <b>Corrección honesta:</b> la primera versión de este comentario justificaba la
  /// elección diciendo que el claim "destruye la garantía de orden que da
  /// <c>Sequence</c>". <b>Esa garantía no existe</b> (ver <see cref="OutboxMessage.Sequence"/>:
  /// el IDENTITY se asigna al INSERT y la fila se ve al COMMIT), así que el argumento era
  /// falso aunque la decisión siga siendo la buena por lo dicho arriba.
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
