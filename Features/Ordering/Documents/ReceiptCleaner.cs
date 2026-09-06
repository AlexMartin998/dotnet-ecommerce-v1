using ApiEcommerce.Shared.Documents;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Documents;


/// <summary>El temporizador del recolector de comprobantes huérfanos. Nada más.</summary>
/// <remarks>
/// Qué se borra lo decide <see cref="IOrphanReceiptCollector"/>, fuera de aquí, para poder
/// probarlo sin esperar horas.
/// </remarks>
public sealed class ReceiptCleaner(
    IServiceScopeFactory scopeFactory,
    IOptions<DocumentStorageOptions> options,
    ILogger<ReceiptCleaner> logger) : BackgroundService
{
  private readonly DocumentStorageOptions _options = options.Value;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (_options.CleanupIntervalHours <= 0)
    {
      logger.LogInformation("Documents:CleanupIntervalHours is 0; orphan collection disabled");
      return;
    }

    var interval = TimeSpan.FromHours(_options.CleanupIntervalHours);

    // Un respiro antes de la primera pasada: el arranque ya tiene bastante sin recorrer el disco.
    try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
    catch (OperationCanceledException) { return; }

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        // Scope propio por pasada: el job es singleton y el recolector es Scoped.
        using var scope = scopeFactory.CreateScope();

        await scope.ServiceProvider
            .GetRequiredService<IOrphanReceiptCollector>()
            .CollectAsync(stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;   // apagado ordenado
      }
      catch (Exception ex)
      {
        // Sin filtro que excluya OperationCanceledException: una que no venga del stoppingToken
        // escaparía, y BackgroundServiceExceptionBehavior.StopHost tumbaría la API entera.
        logger.LogError(ex, "Orphan document collection failed; retrying next cycle");
      }

      try { await Task.Delay(interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }
}
