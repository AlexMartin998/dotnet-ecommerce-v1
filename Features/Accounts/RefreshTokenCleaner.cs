using ApiEcommerce.Features.Accounts.Repository;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Features.Accounts;


/// <summary>
/// Borra los refresh tokens que ya caducaron.
/// </summary>
/// <remarks>
/// <para>
/// La tabla solo crece: cada login abre una familia y cada refresco añade un eslabón.
/// Un usuario activo genera decenas al día, y ninguno sirve para nada pasada su fecha
/// —ni siquiera los revocados, porque la comprobación de reuso solo mira dentro de la
/// ventana de gracia—.
/// </para>
/// <para>
/// ⚠️ <b>Vive en el slice, no en <c>OutboxCleaner</c>.</b> Es tentador meter todas las
/// purgas en el recolector que ya existe, pero ese vive en <c>Shared/Messaging</c> y
/// nombrar ahí una entidad de <c>Accounts</c> invierte la dirección de dependencias que
/// fija <c>rules.md</c> §4: lo transversal no conoce los slices.
/// </para>
/// <para>
/// Se registra <b>siempre</b>, como el resto de purgas: la tabla crece haya o no
/// actividad, y una limpieza que solo corre en algunos entornos es una que nadie recuerda
/// que existe hasta que la tabla estorba.
/// </para>
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
    // Un respiro antes de la primera pasada: al arrancar hay cosas más urgentes
    // (migraciones, seeding).
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
        // ⚠️ SIN filtro que excluya OperationCanceledException: una OCE que no venga del
        // stoppingToken (la cancelación de un SqlCommand) se escaparía y, con
        // BackgroundServiceExceptionBehavior.StopHost por defecto desde .NET 6, tumbaría
        // la API entera por una tarea de limpieza.
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

    // Un margen sobre la caducidad: dentro de la ventana de gracia todavía se consulta un
    // token recién gastado para distinguir una carrera del cliente de un robo. Borrarlo
    // antes convertiría esa distinción en "no existe" y perdería la señal.
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
