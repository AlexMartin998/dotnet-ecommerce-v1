using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ApiEcommerce.Shared.Messaging.RabbitMq;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Drena la tabla outbox hacia el broker.
/// </summary>
/// <remarks>
/// <para>
/// Es un <see cref="BackgroundService"/> y no parte del request: publicar dentro de la
/// petición volvería a atar la latencia (y la disponibilidad) de la compra a la del
/// broker, que es justo lo que el outbox venía a desacoplar.
/// </para>
/// <para>
/// <b>Orden de las operaciones.</b> Primero se publica, luego se marca como procesado.
/// Al revés se perderían mensajes si el proceso muere entremedias. Así, como mucho, se
/// publica dos veces — <b>at-least-once</b>, y por eso el consumidor deduplica.
/// </para>
/// </remarks>
public sealed class OutboxPublisher(
    IServiceScopeFactory scopeFactory,
    IEventPublisher publisher,
    IOptions<RabbitMqOptions> options,
    ILogger<OutboxPublisher> logger) : BackgroundService
{
  private readonly RabbitMqOptions _options = options.Value;

  /// <summary>Mensajes por vuelta. Acotado para no monopolizar la conexión ni la base.</summary>
  private const int BatchSize = 50;

  /// <summary>Intentos antes de dejar de reintentar un mensaje concreto.</summary>
  private const int MaxAttempts = 5;

  protected override async Task ExecuteAsync(CancellationToken stoppingToken)
  {
    if (!_options.IsEnabled)
    {
      logger.LogInformation("RabbitMq:ConnectionString is empty; outbox publisher disabled");
      return;
    }

    var interval = TimeSpan.FromSeconds(_options.PublishIntervalSeconds);

    while (!stoppingToken.IsCancellationRequested)
    {
      try
      {
        await PublishPendingAsync(stoppingToken);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Un fallo aquí NO puede matar el BackgroundService: si el bucle termina,
        // nadie vuelve a publicar hasta que se reinicie el proceso.
        logger.LogError(ex, "Outbox publish loop failed; retrying in {Interval}", interval);
      }

      try { await Task.Delay(interval, stoppingToken); }
      catch (OperationCanceledException) { break; }
    }
  }

  private async Task PublishPendingAsync(CancellationToken ct)
  {
    // Scope propio: un BackgroundService es singleton y no puede inyectar un
    // AppDbContext (scoped) por constructor.
    using var scope = scopeFactory.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var pending = await db.OutboxMessages
        .Where(m => m.ProcessedAt == null && m.Attempts < MaxAttempts)
        .OrderBy(m => m.OccurredAt)     // orden de ocurrencia: los eventos importan en orden
        .Take(BatchSize)
        .ToListAsync(ct);

    if (pending.Count == 0) return;

    foreach (var message in pending)
    {
      try
      {
        await publisher.PublishAsync(message.Id, message.Type, message.Payload, ct);

        message.ProcessedAt = DateTime.Now;
        message.LastError = null;

        logger.LogInformation("Published {EventType} {MessageId}", message.Type, message.Id);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        message.Attempts++;
        message.LastError = ex.Message.Length > 1000 ? ex.Message[..1000] : ex.Message;

        // Traza completa solo cuando el mensaje se agota: los intentos intermedios
        // son ruido esperado mientras el broker esté caído.
        if (message.Attempts >= MaxAttempts)
          logger.LogError(ex,
              "Giving up on {MessageId} after {Attempts} attempts; it stays in the outbox for manual review",
              message.Id, message.Attempts);
        else
          logger.LogWarning(
              "Failed to publish {MessageId} (attempt {Attempts}/{Max}): {Error}",
              message.Id, message.Attempts, MaxAttempts, ex.Message);

        // El broker está caído: no tiene sentido intentar los siguientes de la tanda.
        break;
      }
    }

    await db.SaveChangesAsync(ct);
  }
}
