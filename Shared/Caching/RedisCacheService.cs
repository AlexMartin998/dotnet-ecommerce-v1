using System.Text.Json;
using Microsoft.Extensions.Caching.Distributed;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Caching;


/// <summary>
/// Implementación de <see cref="ICacheService"/> sobre <see cref="IDistributedCache"/>
/// (Redis), serializando a JSON.
/// </summary>
/// <remarks>
/// Falla en abierto: con Redis caído se loguea y se sirve el valor real desde la base, para
/// que una optimización no sea un punto único de fallo. Singleton, porque no guarda estado
/// por request y sus tres dependencias ya lo son.
/// </remarks>
public sealed class RedisCacheService(
    IDistributedCache cache,
    IOptions<CacheOptions> options,
    ILogger<RedisCacheService> logger) : ICacheService
{
  private readonly CacheOptions _options = options.Value;

  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

  /// <inheritdoc />
  public async Task<T> GetOrSetAsync<T>(
      string key, Func<CancellationToken, Task<T>> factory,
      TimeSpan? ttl = null, CancellationToken ct = default)
  {
    ArgumentException.ThrowIfNullOrWhiteSpace(key);
    ArgumentNullException.ThrowIfNull(factory);

    try
    {
      var cached = await cache.GetStringAsync(key, ct);

      if (cached is not null)
      {
        logger.LogDebug("Cache HIT {Key}", key);
        var value = JsonSerializer.Deserialize<T>(cached, SerializerOptions);

        if (value is not null) return value;
      }
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Cache read failed for {Key}; falling back to the source", key);
    }

    logger.LogDebug("Cache MISS {Key}", key);
    var fresh = await factory(ct);

    try
    {
      await cache.SetStringAsync(
          key,
          JsonSerializer.Serialize(fresh, SerializerOptions),
          new DistributedCacheEntryOptions
          {
            AbsoluteExpirationRelativeToNow = ttl ?? TimeSpan.FromSeconds(_options.DefaultTtlSeconds)
          },
          ct);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Cache write failed for {Key}", key);
    }

    return fresh;
  }

  /// <inheritdoc />
  public async Task RemoveAsync(CancellationToken ct = default, params string[] keys)
  {
    foreach (var key in keys)
    {
      try
      {
        await cache.RemoveAsync(key, ct);
        logger.LogDebug("Cache EVICT {Key}", key);
      }
      catch (Exception ex) when (ex is not OperationCanceledException)
      {
        // Una invalidación perdida sirve datos viejos como mucho hasta el TTL: preferible
        // a devolver un 500 en un POST que sí guardó bien.
        logger.LogWarning(ex, "Cache eviction failed for {Key}", key);
      }
    }
  }
}
