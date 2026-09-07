using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Purga las filas ya procesadas de <c>OutboxMessages</c>, <c>ProcessedMessages</c> y
/// <c>ExecutedCommands</c>.
/// </summary>
/// <remarks>
/// Las tres tablas crecen con cada compra y sin techo. Se conserva una ventana
/// (<see cref="OutboxOptions.RetentionDays"/>) porque son la evidencia de qué se publicó, y no
/// se toca nada sin procesar: borrarlo perdería el hecho de negocio en silencio.
/// </remarks>
public sealed class OutboxCleaner(
    IServiceScopeFactory scopeFactory,
    IOptions<OutboxOptions> options,
    ILogger<OutboxCleaner> logger) : BackgroundService
{
  private readonly OutboxOptions _options = options.Value;

  /// <summary>Filas por sentencia, para no hacer un borrado gigante que bloquee la tabla.</summary>
  private const int DeleteBatchSize = 5_000;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var interval = TimeSpan.FromHours(_options.CleanupIntervalHours);

    // Un respiro antes de la primera pasada: al arrancar hay cosas más urgentes.
    try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
    catch (OperationCanceledException) { return; }

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await CleanAsync(stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;   // apagado ordenado
      }
      catch (Exception ex)
      {
        // Sin filtrar OperationCanceledException: una OCE ajena al stoppingToken tumbaría la API (StopHost).
        logger.LogError(ex, "Outbox cleanup failed; retrying in {Interval}", interval);
      }

      try { await Task.Delay(interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }

  private async Task CleanAsync(CancellationToken ct)
  {
    using var scope = scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // El corte va en variable LOCAL: dentro del árbol de expresión sería GETDATE(), otro reloj.
    var cutoff = DateTime.Now.AddDays(-_options.RetentionDays);

    var outbox = await DeleteInBatchesAsync(
        () => db.OutboxMessages
                .Where(m => m.ProcessedAt != null && m.ProcessedAt < cutoff)
                .OrderBy(m => m.Sequence)
                .Take(DeleteBatchSize),
        ct);

    var processed = await DeleteInBatchesAsync(
        () => db.ProcessedMessages
                .Where(m => m.ProcessedAt < cutoff)
                .OrderBy(m => m.ProcessedAt)
                .Take(DeleteBatchSize),
        ct);

    // Los comandos ya ejecutados caducan igual, pero el plazo debe cubrir el PEOR reintento de
    // un cliente: a partir de ahí, la misma Idempotency-Key vuelve a ejecutar de verdad.
    var commands = await DeleteInBatchesAsync(
        () => db.ExecutedCommands
                .Where(c => c.ExecutedAt < cutoff)
                .OrderBy(c => c.ExecutedAt)
                .Take(DeleteBatchSize),
        ct);

    if (outbox + processed + commands > 0)
      logger.LogInformation(
          "Outbox cleanup removed {Outbox} outbox row(s), {Processed} processed-message row(s) and {Commands} executed-command row(s) older than {Cutoff:u}",
          outbox, processed, commands, cutoff);
  }

  /// <summary>
  /// Borra en tandas hasta que no queda nada que borrar.
  /// </summary>
  /// <remarks>
  /// <c>ExecuteDeleteAsync</c> no trae las filas al cliente. Se trocea porque un borrado de
  /// cientos de miles escala el bloqueo a toda la tabla y frena a las compras.
  /// </remarks>
  private static async Task<int> DeleteInBatchesAsync<T>(
      Func<IQueryable<T>> batch, CancellationToken ct) where T : class
  {
    var total = 0;

    while (!ct.IsCancellationRequested)
    {
      var deleted = await batch().ExecuteDeleteAsync(ct);
      total += deleted;

      if (deleted < DeleteBatchSize) break;
    }

    return total;
  }
}
