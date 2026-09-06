using ApiEcommerce.Shared.Caching;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// La puerta de concurrencia, sobre Redis, con <c>SET ... NX</c>.
/// </summary>
/// <remarks>
/// Usa <see cref="IConnectionMultiplexer"/> y no <c>IDistributedCache</c> porque esa
/// abstracción no tiene «set si no existe». Falla en abierto: lo que se pierde es el
/// atajo, no la garantía, que arbitra la clave primaria de <c>ExecutedCommands</c>.
/// </remarks>
public sealed class RedisIdempotencyStore(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> cacheOptions,
    ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore
{
  private readonly string _prefix = cacheOptions.Value.InstanceName + "idem:";

  /// <summary>Suelta el marcador solo si sigue siendo del que lo puso.</summary>
  /// <remarks>
  /// Sin la comprobación, una petición cuyo marcador ya caducó borraría el de otra que sí
  /// está en vuelo y la puerta dejaría pasar a un tercero.
  /// </remarks>
  private const string ReleaseScript = """
      if redis.call('GET', KEYS[1]) == ARGV[1] then
        return redis.call('DEL', KEYS[1])
      end
      return 0
      """;

  /// <inheritdoc />
  public async Task<IdempotencyGate> TryEnterAsync(
      string key, TimeSpan ttl, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    var fence = Guid.NewGuid().ToString("N");

    try
    {
      var entered = await redis.GetDatabase().StringSetAsync(
          _prefix + key, fence, ttl, When.NotExists);

      return entered ? IdempotencyGate.Entered(fence) : IdempotencyGate.Busy;
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Sin atajo, pero no sin garantía: la petición sigue y ExecutedCommands arbitra.
      logger.LogWarning(ex,
          "Idempotency gate unavailable for {Key}; falling through to the transactional guarantee", key);

      return IdempotencyGate.Unavailable;
    }
  }

  /// <inheritdoc />
  public async Task ReleaseAsync(string key, string fence, CancellationToken ct = default)
  {
    // Sin token no pusimos marcador: no hay nada nuestro que soltar.
    if (string.IsNullOrEmpty(fence)) return;

    try
    {
      await redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [_prefix + key], [fence]);
    }
    catch (Exception ex)
    {
      // Corre también en el camino de error: si lanzara, taparía la excepción real del
      // servicio. Lo peor que pasa es que el marcador caduque solo.
      logger.LogWarning(ex, "Could not release the idempotency gate for {Key}", key);
    }
  }
}
