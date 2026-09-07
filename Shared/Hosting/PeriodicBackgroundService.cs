namespace ApiEcommerce.Shared.Hosting;


/// <summary>
/// Trabajo de fondo que se repite cada cierto tiempo, con el blindaje que un
/// <see cref="BackgroundService"/> necesita para no tumbar la aplicación.
/// </summary>
/// <remarks>
/// Herencia y no composición: esto es MECANISMO —el bucle, el apagado ordenado y el catch
/// sin filtro— sin ninguna decisión de negocio. Una excepción que escape de
/// <see cref="ExecuteAsync"/> mata el servicio para siempre y, con el
/// <c>StopHost</c> que es el default desde .NET 6, se lleva la API entera por delante.
/// </remarks>
public abstract class PeriodicBackgroundService(ILogger logger) : BackgroundService
{
  /// <summary>Cada cuánto se repite. Cero o menos apaga el trabajo.</summary>
  protected abstract TimeSpan Interval { get; }

  /// <summary>Cuánto se espera antes de la primera pasada. Cero arranca de inmediato.</summary>
  protected virtual TimeSpan StartDelay => TimeSpan.Zero;

  /// <summary>Lo que se registra cuando una pasada falla, sin punto final.</summary>
  protected abstract string FailureMessage { get; }

  /// <summary>Lo que se registra si el trabajo está apagado por configuración.</summary>
  protected virtual string DisabledMessage => $"{GetType().Name} is disabled";

  /// <summary>Una pasada. Que lance es normal: el bucle lo registra y reintenta.</summary>
  protected abstract Task RunOnceAsync(CancellationToken ct);

  protected sealed override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    var interval = Interval;

    if (interval <= TimeSpan.Zero)
    {
      logger.LogInformation("{Message}", DisabledMessage);
      return;
    }

    if (!await DelayAsync(StartDelay, stoppingToken)) return;

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await RunOnceAsync(stoppingToken);
      }
      catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
      {
        break;   // apagado ordenado
      }
      catch (Exception ex)
      {
        // Sin filtro que excluya OperationCanceledException: una que no venga del
        // stoppingToken se escaparía y mataría el servicio.
        logger.LogError(ex, "{Message}; retrying in {Interval}", FailureMessage, interval);
      }

      if (!await DelayAsync(interval, stoppingToken)) break;
    }
  }

  /// <summary><c>false</c> si la espera se interrumpió porque hay que parar.</summary>
  private static async Task<bool> DelayAsync(TimeSpan delay, CancellationToken ct)
  {
    if (delay <= TimeSpan.Zero) return !ct.IsCancellationRequested;

    try
    {
      await Task.Delay(delay, ct);
      return true;
    }
    catch (OperationCanceledException)
    {
      return false;
    }
  }
}
