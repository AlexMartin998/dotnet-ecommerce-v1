using System.Data;
using ApiEcommerce.Data;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using Microsoft.Extensions.Options;
using ApiEcommerce.Shared.Db;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>Drena la tabla outbox hacia el broker.</summary>
/// <remarks>
/// Publica primero y marca después: así, como mucho, se publica dos veces (at-least-once). Un
/// broker caído no gasta intentos —solo el fallo atribuible a un mensaje concreto, ver
/// <see cref="BrokerUnavailableException"/>— y un <c>sp_getapplock</c> deja drenar a una réplica.
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
        // Sin filtrar OperationCanceledException: una OCE ajena al stoppingToken tumbaría la API (StopHost).
        logger.LogError(ex, "Outbox publish loop failed; retrying in {Interval}", interval);
      }

      try { await Task.Delay(interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }

  private async Task PublishPendingAsync(CancellationToken ct)
  {
    // Scope propio: un BackgroundService es singleton y el AppDbContext es scoped.
    using var scope = scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
    var transactions = scope.ServiceProvider.GetRequiredService<ITransactionRunner>();

    // Presupuesto para la tanda: la transacción —y con ella el applock— sigue abierta durante
    // todo el diálogo con el broker, y un broker mudo la mantendría abierta minutos.
    using var budget = CancellationTokenSource.CreateLinkedTokenSource(ct);
    budget.CancelAfter(TimeSpan.FromSeconds(_options.PublishIntervalSeconds * 2));

    // Fuera del delegado a propósito: la execution strategy puede reejecutarlo entero, y las
    // publicaciones ya hechas al broker no se deshacen.
    var alreadyPublished = new HashSet<Guid>();

    try
    {
      await transactions.ExecuteAsync(async token =>
      {
        // Si otra réplica está drenando, esta vuelta no hace nada: el trabajo no se pierde.
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
      // Se agotó el presupuesto, no el apagado: se suelta la transacción y se reintenta después.
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
        // Si la estrategia reejecutó el delegado, esto ya salió al broker: se marca sin republicar.
        if (!alreadyPublished.Contains(message.Id))
        {
          await publisher.PublishAsync(message.Id, message.Type, message.Payload, ct);
          alreadyPublished.Add(message.Id);
        }

        // Se marca DESPUÉS de publicar: al revés, morir entremedias pierde el mensaje.
        message.ProcessedAt = DateTime.Now;
        message.LastError = null;
        published++;

        logger.LogInformation("Published {EventType} {MessageId}", message.Type, message.Id);
      }
      catch (BrokerUnavailableException ex)
      {
        // El broker caído NO consume intentos: le pasa igual a todos los mensajes, y contarlo
        // aquí enterraba eventos válidos. Se corta la tanda sin tocar Attempts; lo publicado se guarda.
        logger.LogWarning(
            "Broker unavailable ({Error}); {Pending} event(s) stay in the outbox, no attempt consumed",
            ex.Message, pending.Count - published);
        break;
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Aquí sí: el fallo es atribuible al mensaje (payload ilegible, sin cola destino…).
        message.Attempts++;
        message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;

        // Traza completa solo cuando el mensaje se agota: los intentos intermedios son ruido.
        if (message.Attempts >= maxAttempts)
          logger.LogError(ex,
              "Giving up on {MessageId} after {Attempts} attempts; it stays in the outbox for manual review",
              message.Id, message.Attempts);
        else
          logger.LogWarning(
              "Failed to publish {MessageId} (attempt {Attempts}/{Max}): {Error}",
              message.Id, message.Attempts, maxAttempts, ex.Message);

        // `continue` y no `break`: un mensaje envenenado no debe bloquear la cabecera de la tanda.
        continue;
      }
    }

    await db.SaveChangesAsync(ct);

    return published;
  }

  /// <summary>
  /// Toma un bloqueo aplicativo exclusivo para que solo una réplica drene a la vez.
  /// </summary>
  /// <remarks>
  /// Sin él, dos réplicas leen el mismo lote y doblan el tráfico (no corrompe nada, el consumidor
  /// deduplica). Se prefiere al claim por filas porque no obliga a gestionar expiraciones, y
  /// <c>@LockOwner = 'Transaction'</c> lo suelta al commit aunque muera el proceso.
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
    // Timeout 0: si otro lo tiene, se salta la vuelta en vez de encolar réplicas esperando.
    command.Parameters.Add(new SqlParameter("@LockTimeout", 0));

    var result = new SqlParameter { ParameterName = "@Result", SqlDbType = SqlDbType.Int, Direction = ParameterDirection.ReturnValue };
    command.Parameters.Add(result);

    await command.ExecuteNonQueryAsync(ct);

    // >= 0 concedido (0 inmediato, 1 tras esperar); < 0 no concedido.
    return result.Value is int code && code >= 0;
  }
}
