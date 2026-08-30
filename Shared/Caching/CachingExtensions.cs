namespace ApiEcommerce.Shared.Caching;


public static class CachingExtensions
{
  /// <summary>
  /// Cache distribuida en Redis, o el Null Object si no hay Redis configurado.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Leer la configuración aquí, de forma <b>eager</b>, es correcto e inevitable: lo
  /// que se decide es <i>qué implementación se registra</i>, y el grafo de DI se
  /// construye una sola vez al arrancar. No es lo mismo que leer configuración en
  /// caliente dentro de un servicio, que sí debe ir por <c>IOptionsMonitor</c>.
  /// </para>
  /// <para>
  /// <b>Las dos ramas registran el mismo lifetime a propósito.</b> Antes una era
  /// <c>Scoped</c> (Redis) y la otra <c>Singleton</c> (sin cache): eso es una mina,
  /// porque un consumidor singleton funcionaría en la máquina sin Redis y reventaría
  /// con captured dependency justo en el entorno que sí la tiene. Ambas son
  /// <c>Singleton</c> porque ninguna guarda estado por request y las dependencias de
  /// <see cref="RedisCacheService"/> (<c>IDistributedCache</c>, <c>IOptions</c>,
  /// <c>ILogger</c>) ya son singletons.
  /// </para>
  /// </remarks>
  public static IServiceCollection AddDistributedCaching(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<CacheOptions>()
        .Bind(configuration.GetSection(CacheOptions.SectionName))
        .ValidateDataAnnotations();

    var options = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()
                  ?? new CacheOptions();

    if (options.IsEnabled)
    {
      services.AddStackExchangeRedisCache(redis =>
      {
        redis.Configuration = options.Configuration;
        redis.InstanceName = options.InstanceName;
      });

      services.AddSingleton<ICacheService, RedisCacheService>();
    }
    else
    {
      // Null Object: los decoradores siguen compilando y ejecutando sin un solo `if`.
      services.AddSingleton<ICacheService, NoCacheService>();
    }

    return services;
  }
}
