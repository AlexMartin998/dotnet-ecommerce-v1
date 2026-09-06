using ApiEcommerce.Shared.Documents;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Documents;


/// <summary>
/// El temporizador del recolector de comprobantes huérfanos. Nada más.
/// </summary>
/// <remarks>
/// Todo lo que decide qué se borra vive en <see cref="IOrphanReceiptCollector"/>, fuera de
/// aquí, para que se pueda probar sin esperar horas. Esta clase solo aporta el reloj y la
/// disciplina de no tumbar el proceso.
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

    // Un respiro antes de la primera pasada: el arranque ya tiene bastante —migraciones,
    // seeding, la primera conexión al broker— sin que además alguien recorra el disco.
    try { await Task.Delay(TimeSpan.FromMinutes(5), stoppingToken); }
    catch (OperationCanceledException) { return; }

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        // Scope propio por pasada: este job es un singleton y el recolector es Scoped
        // (depende del repositorio, que depende del DbContext).
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
        // SIN filtro que excluya OperationCanceledException: una OCE que NO venga del
        // stoppingToken escaparía de ExecuteAsync, y desde .NET 6 el default es
        // BackgroundServiceExceptionBehavior.StopHost — un fallo recogiendo basura
        // tumbaría la API entera.
        logger.LogError(ex, "Orphan document collection failed; retrying next cycle");
      }

      try { await Task.Delay(interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }
}
