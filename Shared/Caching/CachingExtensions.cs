using ApiEcommerce.Shared.Idempotency;
using StackExchange.Redis;

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
        .ValidateDataAnnotations()
        .ValidateOnStart();   // configuración inválida = no arranca, no falla en la primera petición

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

      // Conexión cruda a Redis, además de IDistributedCache: la idempotencia
      // necesita `SET NX` (reservar si no existe) y esa primitiva no existe en
      // IDistributedCache. El multiplexer es thread-safe y caro de crear: se
      // comparte como singleton, que es como lo recomienda StackExchange.Redis.
      services.AddSingleton<IConnectionMultiplexer>(_ =>
      {
        var config = ConfigurationOptions.Parse(options.Configuration);

        // AbortOnConnectFail es `true` por defecto, y la fábrica es PEREZOSA: se
        // ejecuta en la primera petición que necesite el store, no al arrancar. Con
        // Redis caído en ese instante, la fábrica lanzaba, el contenedor NO cachea
        // instancias fallidas, y cada petición siguiente reintentaba una conexión
        // bloqueante de 5 s. Con `false`, conecta en segundo plano y se recupera solo.
        config.AbortOnConnectFail = false;
        config.ConnectRetry = 3;

        return ConnectionMultiplexer.Connect(config);
      });

      services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();
    }
    else
    {
      // Null Object: los decoradores siguen compilando y ejecutando sin un solo `if`.
      services.AddSingleton<ICacheService, NoCacheService>();
      services.AddSingleton<IIdempotencyStore, NoIdempotencyStore>();
    }

    return services;
  }
}
