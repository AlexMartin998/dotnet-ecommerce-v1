using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Purga las filas ya procesadas de <c>OutboxMessages</c> y <c>ProcessedMessages</c>.
/// </summary>
/// <remarks>
/// <para>
/// Ambas tablas crecen <b>con cada compra y para siempre</b>. El índice de pendientes es
/// filtrado, así que la consulta del publicador no se degrada; lo que crece sin techo es
/// el disco, el tiempo de las copias de seguridad y el coste de cualquier
/// <c>ALTER TABLE</c> futuro. Es la clase de deuda que no molesta hasta que ya es cara.
/// </para>
/// <para>
/// Se conserva una ventana (<see cref="OutboxOptions.RetentionDays"/>) en vez de borrar
/// al procesar: son la evidencia de qué se publicó y qué se consumió cuando alguien
/// pregunta por un pedido de la semana pasada.
/// </para>
/// <para>
/// ⚠️ <b>No se toca nada sin procesar.</b> El filtro de <c>OutboxMessages</c> exige
/// <c>ProcessedAt != null</c>: un evento que agotó sus reintentos sigue pendiente de
/// revisión manual y borrarlo sería perder el hecho de negocio en silencio, que es
/// justamente lo que el outbox existe para impedir.
/// </para>
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

    // Un respiro antes de la primera pasada: al arrancar hay cosas más urgentes
    // (migraciones, seeding, el primer drenaje del outbox).
    try { await Task.Delay(TimeSpan.FromMinutes(1), stoppingToken); }
    catch (OperationCanceledException) { return; }

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await CleanAsync(stoppingToken);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Mismo motivo que en el publicador: si el bucle muere, nadie vuelve a purgar
        // hasta el siguiente reinicio, y desde .NET 6 una excepción que escapa de
        // ExecuteAsync tumba el host entero.
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

    // ⚠️ El corte se calcula en una VARIABLE LOCAL. Dentro del árbol de expresión,
    // DateTime.Now se traduce a GETDATE() y lo evaluaría el reloj del servidor SQL —
    // otro reloj distinto del que escribió las filas.
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

    if (outbox + processed > 0)
      logger.LogInformation(
          "Outbox cleanup removed {Outbox} outbox row(s) and {Processed} processed-message row(s) older than {Cutoff:u}",
          outbox, processed, cutoff);
  }

  /// <summary>
  /// Borra en tandas hasta que no queda nada que borrar.
  /// </summary>
  /// <remarks>
  /// <c>ExecuteDeleteAsync</c> lanza un único <c>DELETE</c> sin traer las filas al
  /// cliente. Se trocea porque un borrado de cientos de miles de filas escala el bloqueo
  /// a toda la tabla y bloquea a las compras que están escribiendo su evento.
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
