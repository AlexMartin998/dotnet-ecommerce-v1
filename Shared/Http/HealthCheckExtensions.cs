using ApiEcommerce.Data;
using ApiEcommerce.Shared.Caching;

namespace ApiEcommerce.Shared.Http;


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

    return services;
  }
}
