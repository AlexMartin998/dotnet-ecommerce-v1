using ApiEcommerce.Shared.Hosting;
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
    ILogger<ReceiptCleaner> logger) : PeriodicBackgroundService(logger)
{
  private readonly DocumentStorageOptions _options = options.Value;

  protected override TimeSpan Interval => TimeSpan.FromHours(_options.CleanupIntervalHours);

  /// <summary>Un respiro antes de la primera: el arranque ya tiene bastante sin recorrer el disco.</summary>
  protected override TimeSpan StartDelay => TimeSpan.FromMinutes(5);

  protected override string FailureMessage => "Orphan document collection failed";

  protected override string DisabledMessage
      => "Documents:CleanupIntervalHours is 0; orphan collection disabled";

  protected override async Task RunOnceAsync(CancellationToken ct)
  {
    // Scope propio por pasada: el job es singleton y el recolector es Scoped.
    using var scope = scopeFactory.CreateScope();

    await scope.ServiceProvider
        .GetRequiredService<IOrphanReceiptCollector>()
        .CollectAsync(ct);
  }
}
