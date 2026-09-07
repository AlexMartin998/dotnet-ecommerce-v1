using ApiEcommerce.Shared.Idempotency;
using StackExchange.Redis;
using ApiEcommerce.Shared.Auth;

namespace ApiEcommerce.Shared.Caching;


/// <summary>Registro en DI de la cache distribuida y de lo que cuelga de Redis.</summary>
public static class CachingExtensions
{
  /// <summary>
  /// Cache distribuida en Redis, o el Null Object si no hay Redis configurado.
  /// </summary>
  /// <remarks>
  /// Leer la configuración de forma eager es correcto aquí porque lo que se decide es qué
  /// implementación se registra. Las dos ramas usan el mismo lifetime a propósito: mezclar
  /// Scoped y Singleton es una captured dependency que solo falla donde hay Redis.
  /// </remarks>
  public static IServiceCollection AddDistributedCaching(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<CacheOptions>()
        .Bind(configuration.GetSection(CacheOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();   // configuración inválida = no arranca, no falla en la primera petición

    // Los plazos de la idempotencia se registran siempre, haya Redis o no: el filtro los
    // lee aunque el store sea el Null Object.
    services.AddOptions<IdempotencyOptions>()
        .Bind(configuration.GetSection(IdempotencyOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddSingleton<IdempotencyMetrics>();

    var options = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>()
                  ?? new CacheOptions();

    if (options.IsEnabled)
    {
      // Una sola conexión para las cuatro cosas que hablan con Redis (cache, idempotencia,
      // denylist y sonda): con el multiplexer que AddStackExchangeRedisCache crea por su
      // cuenta, la cache se quedaba con los timeouts de fábrica.
      var multiplexer = new Lazy<IConnectionMultiplexer>(() => Connect(options.Configuration));

      services.AddStackExchangeRedisCache(redis =>
      {
        redis.InstanceName = options.InstanceName;
        // Con ConnectionMultiplexerFactory, `Configuration` se ignora: la conexión la
        // ponemos nosotros, con nuestros timeouts.
        redis.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer.Value);
      });

      services.AddSingleton<ICacheService, RedisCacheService>();

      // Conexión cruda además de IDistributedCache: la idempotencia necesita `SET NX`, que
      // esa abstracción no expone. El multiplexer es thread-safe y caro de crear.
      services.AddSingleton<IConnectionMultiplexer>(_ => multiplexer.Value);

      services.AddSingleton<IIdempotencyStore, RedisIdempotencyStore>();

      // La denylist de access tokens. Singleton como el resto: no guarda estado por
      // request y sus dependencias ya son singletons.
      services.AddSingleton<IAccessTokenDenylist, RedisAccessTokenDenylist>();
    }
    else
    {
      // Null Object: los decoradores siguen compilando y ejecutando sin un solo `if`.
      services.AddSingleton<ICacheService, NoCacheService>();
      services.AddSingleton<IIdempotencyStore, NoIdempotencyStore>();
      services.AddSingleton<IAccessTokenDenylist, NoAccessTokenDenylist>();
    }

    return services;
  }

  private static IConnectionMultiplexer Connect(string configuration)
  {
        var config = ConfigurationOptions.Parse(configuration);

        // La fábrica es perezosa: con `AbortOnConnectFail` en true y Redis caído en ese
        // instante, cada petición reintentaba una conexión bloqueante de 5 s. Con false,
        // conecta en segundo plano y se recupera solo.
        config.AbortOnConnectFail = false;

        // Los timeouts de fábrica convierten «degradar en abierto» en una caída: con ellos
        // un GET del catálogo tardaba 11 s y una compra con Idempotency-Key, 34 s.
        config.ConnectRetry = 1;
        config.ConnectTimeout = 1000;
        config.SyncTimeout = 1000;
        config.AsyncTimeout = 1000;

        // Sin esto los timeouts no bastan: la política por defecto ENCOLA los comandos
        // mientras la conexión está caída y cada uno espera su timeout. Medido con Redis
        // muerto: 2 s por llamada y 4 s en una lectura cache-aside, que hace dos. Fallar
        // rápido es lo que convierte una cache caída en un atajo que no está.
        config.BacklogPolicy = BacklogPolicy.FailFast;

    return ConnectionMultiplexer.Connect(config);
  }
}
