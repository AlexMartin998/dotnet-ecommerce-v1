using ApiEcommerce.Data;
using ApiEcommerce.Shared.Caching;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Http.Health;


public static class HealthCheckExtensions
{
  /// <summary>
  /// Sondas de salud. <c>/health</c> es <b>liveness</b> (¿el proceso responde?) y lo
  /// sirve <c>HealthController</c>; <c>/health/ready</c> es <b>readiness</b> (¿las
  /// dependencias están arriba?) y es el que debe mirar un orquestador antes de
  /// mandarle tráfico.
  /// </summary>
  public static IServiceCollection AddHealthProbes(
      this IServiceCollection services, IConfiguration configuration)
  {
    var checks = services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>(name: "sqlserver", tags: ["ready"]);

    var redis = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>();

    // Solo se comprueba Redis si está configurado: si no lo está, la app funciona
    // sin cache y exigirlo en readiness dejaría el servicio fuera de rotación por
    // una dependencia que ni siquiera usa.
    if (redis is not null && redis.IsEnabled)
      checks.AddRedis(redis.Configuration, name: "redis", tags: ["ready"]);

    // Eventos que agotaron sus reintentos y quedaron sin publicar. Sin esta sonda, un
    // broker caído el tiempo suficiente entierra eventos en silencio y el servicio
    // sigue reportándose sano mientras los datos divergen.
    //
    // Degraded y no Unhealthy: la API atiende peticiones perfectamente, lo que hay es
    // trabajo pendiente que alguien tiene que mirar. Marcarlo Unhealthy sacaría de
    // rotación un proceso sano.
    checks.AddCheck<OutboxBacklogHealthCheck>(
        "outbox-backlog", failureStatus: HealthStatus.Degraded, tags: ["ready"]);

    return services;
  }
}


/// <summary>Cuenta los eventos del outbox que se dieron por perdidos.</summary>
/// <remarks>
/// El umbral se lee de <see cref="RabbitMqOptions.MaxPublishAttempts"/>, el MISMO valor
/// que aplica el publicador. Antes era una <c>const</c> aquí con un comentario pidiendo
/// mantenerla sincronizada: subir el máximo en el publicador habría dejado esta sonda
/// contando como perdidos mensajes que aún se estaban reintentando.
/// </remarks>
public sealed class OutboxBacklogHealthCheck(
    AppDbContext db, IOptions<RabbitMqOptions> options) : IHealthCheck
{
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
