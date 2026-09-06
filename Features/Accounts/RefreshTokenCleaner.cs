using ApiEcommerce.Features.Accounts.Repository;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Accounts;


/// <summary>Servicio de fondo que borra periódicamente los refresh tokens ya caducados.</summary>
/// <remarks>
/// Vive en el slice y no junto a las demás purgas: esas están en <c>Shared/</c>, y nombrar allí
/// una entidad de <c>Accounts</c> invertiría la dirección de dependencias.
/// </remarks>
public sealed class RefreshTokenCleaner(
    IServiceScopeFactory scopeFactory,
    IOptions<RefreshTokenOptions> options,
    ILogger<RefreshTokenCleaner> logger) : BackgroundService
{
  private readonly RefreshTokenOptions _options = options.Value;

  private static readonly TimeSpan Interval = TimeSpan.FromHours(6);

  /// <summary>Filas por sentencia, para no bloquear la tabla entera.</summary>
  private const int DeleteBatchSize = 5_000;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    // Un respiro antes de la primera pasada: al arrancar hay cosas más urgentes.
    try { await Task.Delay(TimeSpan.FromMinutes(2), stoppingToken); }
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
        // Sin filtro que excluya OperationCanceledException: una que no venga del stoppingToken
        // se escaparía y, con StopHost por defecto, tumbaría la API por una tarea de limpieza.
        logger.LogError(ex, "Refresh token cleanup failed; retrying in {Interval}", Interval);
      }

      try { await Task.Delay(Interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }

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
