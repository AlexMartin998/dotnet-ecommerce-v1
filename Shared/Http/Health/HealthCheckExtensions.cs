using ApiEcommerce.Data;
using ApiEcommerce.Shared.Caching;
using StackExchange.Redis;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ApiEcommerce.Shared.Messaging;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Http.Health;


/// <summary>Registro en DI de las sondas de salud.</summary>
public static class HealthCheckExtensions
{
  /// <summary>
  /// Sondas de salud: <c>/health</c> es liveness (lo sirve <c>HealthController</c>) y
  /// <c>/health/ready</c> es readiness, la que debe mirar un orquestador.
  /// </summary>
  public static IServiceCollection AddHealthProbes(
      this IServiceCollection services, IConfiguration configuration)
  {
    var checks = services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>(name: "sqlserver", tags: ["ready"]);

    var redis = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>();

    // Solo se comprueba Redis si está configurado, y con el multiplexer del contenedor: con
    // la cadena de conexión la sonda abriría la suya y podría decir "Healthy" mientras la
    // conexión que usa la aplicación está rota.
    //
    // Degraded y no Unhealthy: Redis es una optimización y la app degrada en abierto, pero
    // su caída la ven todas las réplicas a la vez, así que un 503 aquí las sacaba de
    // rotación todas. Degraded responde 200 y sigue visible en el detalle de la sonda.
    if (redis is not null && redis.IsEnabled)
      checks.AddRedis(
          sp => sp.GetRequiredService<IConnectionMultiplexer>(),
          name: "redis", failureStatus: HealthStatus.Degraded, tags: ["ready"]);

    // Sin esta sonda, un broker caído el tiempo suficiente entierra eventos en silencio.
    // Degraded porque la API atiende bien: lo que hay es trabajo pendiente que mirar.
    checks.AddCheck<OutboxBacklogHealthCheck>(
        "outbox-backlog", failureStatus: HealthStatus.Degraded, tags: ["ready"]);

    return services;
  }
}


/// <summary>Cuenta los eventos del outbox que se dieron por perdidos.</summary>
/// <remarks>
/// El umbral se lee de <see cref="OutboxOptions.MaxPublishAttempts"/>, el mismo valor que
/// aplica el publicador: con una constante propia, subirlo allí dejaba esta sonda contando
/// como perdidos mensajes que aún se reintentaban.
/// </remarks>
public sealed class OutboxBacklogHealthCheck(
    AppDbContext db, IOptions<OutboxOptions> options) : IHealthCheck
{
  /// <inheritdoc />
  public async Task<HealthCheckResult> CheckHealthAsync(
      HealthCheckContext context, CancellationToken cancellationToken = default)
  {
    var maxAttempts = options.Value.MaxPublishAttempts;

    var abandoned = await db.OutboxMessages
        .CountAsync(m => m.ProcessedAt == null && m.Attempts >= maxAttempts, cancellationToken);

    return abandoned == 0
        ? HealthCheckResult.Healthy("No abandoned outbox messages")
        : new HealthCheckResult(
            context.Registration.FailureStatus,
            $"{abandoned} outbox message(s) exhausted their retries and need manual review");
  }
}
