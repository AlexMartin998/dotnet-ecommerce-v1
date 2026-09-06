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
      // UNA sola conexión para las dos cosas que hablan con Redis.
      //
      // Antes había dos multiplexers: el que crea AddStackExchangeRedisCache por su
      // cuenta a partir del string de conexión, y el nuestro. Además de duplicar
      // conexiones al broker, el de la cache se quedaba con los timeouts POR DEFECTO,
      // así que los de abajo solo protegían la mitad del sistema — medido: acortarlos
      // bajó la compra con Idempotency-Key de 34 s a 11 s, y esos 11 s que quedaban
      // eran justamente la cache esperando con sus 5 s de fábrica.
      var multiplexer = new Lazy<IConnectionMultiplexer>(() => Connect(options.Configuration));

      services.AddStackExchangeRedisCache(redis =>
      {
        redis.InstanceName = options.InstanceName;
        // Con ConnectionMultiplexerFactory, `Configuration` se ignora: la conexión la
        // ponemos nosotros, con nuestros timeouts.
        redis.ConnectionMultiplexerFactory = () => Task.FromResult(multiplexer.Value);
      });

      services.AddSingleton<ICacheService, RedisCacheService>();

      // Conexión cruda a Redis, además de IDistributedCache: la idempotencia
      // necesita `SET NX` (reservar si no existe) y esa primitiva no existe en
      // IDistributedCache. El multiplexer es thread-safe y caro de crear: se
      // comparte como singleton, que es como lo recomienda StackExchange.Redis.
      services.AddSingleton<IConnectionMultiplexer>(_ => multiplexer.Value);

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

  private static IConnectionMultiplexer Connect(string configuration)
  {
        var config = ConfigurationOptions.Parse(configuration);

        // AbortOnConnectFail es `true` por defecto, y la fábrica es PEREZOSA: se
        // ejecuta en la primera petición que necesite el store, no al arrancar. Con
        // Redis caído en ese instante, la fábrica lanzaba, el contenedor NO cachea
        // instancias fallidas, y cada petición siguiente reintentaba una conexión
        // bloqueante de 5 s. Con `false`, conecta en segundo plano y se recupera solo.
        config.AbortOnConnectFail = false;

        // ⚠️ Los timeouts POR DEFECTO convierten "degradar en abierto" en una caída.
        // Medido con los tests de degradación y Redis inalcanzable: con los valores de
        // fábrica (ConnectTimeout 5 s × ConnectRetry 3, SyncTimeout 5 s) un GET del
        // catálogo tardaba **11 s** y una compra con Idempotency-Key **34 s**. La
        // petición acababa respondiendo bien, pero a esa latencia el cliente ya ha
        // cortado, los hilos se acumulan y la caída de una OPTIMIZACIÓN se lleva por
        // delante toda la API.
        //
        // Con estos valores el peor caso por operación queda acotado a ~1 s. La cache
        // es un atajo: si no contesta rápido, no sirve para nada esperarla.
        config.ConnectRetry = 1;
        config.ConnectTimeout = 1000;
        config.SyncTimeout = 1000;
        config.AsyncTimeout = 1000;

    return ConnectionMultiplexer.Connect(config);
  }
}
