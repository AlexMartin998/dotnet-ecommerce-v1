using ApiEcommerce.Features.Payments;
using ApiEcommerce.Shared.Hosting;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Ordering.Service;


/// <summary>El temporizador del recolector de órdenes abandonadas. Nada más.</summary>
/// <remarks>
/// Qué se cancela lo decide <see cref="IAbandonedOrderCollector"/>, fuera de aquí.
/// </remarks>
public sealed class AbandonedOrderCleaner(
    IServiceScopeFactory scopeFactory,
    IOptions<PaymentOptions> options,
    ILogger<AbandonedOrderCleaner> logger) : PeriodicBackgroundService(logger)
{
  private readonly PaymentOptions _options = options.Value;

  protected override TimeSpan Interval => TimeSpan.FromMinutes(_options.CleanupIntervalMinutes);

  /// <summary>Un respiro antes de la primera pasada: al arrancar hay cosas más urgentes.</summary>
  protected override TimeSpan StartDelay => TimeSpan.FromMinutes(1);

  protected override string FailureMessage => "Abandoned order collection failed";

  protected override string DisabledMessage
      => "Payments:CleanupIntervalMinutes is 0; abandoned order collection disabled";

  protected override async Task RunOnceAsync(CancellationToken ct)
  {
    // Scope propio por pasada: el job es singleton y el recolector es Scoped.
    using var scope = scopeFactory.CreateScope();

    await scope.ServiceProvider
        .GetRequiredService<IAbandonedOrderCollector>()
        .CollectAsync(ct);
  }
}
