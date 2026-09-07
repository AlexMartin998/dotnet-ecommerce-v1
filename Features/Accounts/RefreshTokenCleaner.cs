using ApiEcommerce.Shared.Hosting;
using ApiEcommerce.Features.Accounts.Repository;

namespace ApiEcommerce.Features.Accounts;


/// <summary>Servicio de fondo que borra periódicamente los refresh tokens ya caducados.</summary>
/// <remarks>
/// Vive en el slice y no junto a las demás purgas: esas están en <c>Shared/</c>, y nombrar allí
/// una entidad de <c>Accounts</c> invertiría la dirección de dependencias.
/// </remarks>
public sealed class RefreshTokenCleaner(
    IServiceScopeFactory scopeFactory,
    ILogger<RefreshTokenCleaner> logger) : PeriodicBackgroundService(logger)
{

  /// <summary>Filas por sentencia, para no bloquear la tabla entera.</summary>
  private const int DeleteBatchSize = 5_000;

  protected override TimeSpan Interval => TimeSpan.FromHours(6);

  /// <summary>Un respiro antes de la primera pasada: al arrancar hay cosas más urgentes.</summary>
  protected override TimeSpan StartDelay => TimeSpan.FromMinutes(2);

  protected override string FailureMessage => "Refresh token cleanup failed";

  protected override Task RunOnceAsync(CancellationToken ct) => CleanAsync(ct);

  private async Task CleanAsync(CancellationToken ct)
  {
    using var scope = scopeFactory.CreateScope();
    var repository = scope.ServiceProvider.GetRequiredService<IRefreshTokenRepository>();

    // Un margen sobre la caducidad: la detección de reuso todavía consulta tokens gastados,
    // y borrarlos antes convertiría esa señal en un simple "no existe".
    var cutoff = DateTime.Now.AddDays(-1);

    var deleted = 0;
    int batch;

    do
    {
      batch = await repository.DeleteExpiredBeforeAsync(cutoff, DeleteBatchSize, ct);
      deleted += batch;
    }
    while (batch == DeleteBatchSize && !ct.IsCancellationRequested);

    if (deleted > 0)
      logger.LogInformation("Refresh token cleanup removed {Count} expired token(s)", deleted);
  }
}
