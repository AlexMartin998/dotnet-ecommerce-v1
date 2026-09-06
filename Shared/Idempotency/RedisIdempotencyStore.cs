using ApiEcommerce.Shared.Caching;
using Microsoft.Extensions.Options;
using StackExchange.Redis;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// La puerta de concurrencia, sobre Redis, con <c>SET ... NX</c>.
/// </summary>
/// <remarks>
/// <para>
/// Usa <see cref="IConnectionMultiplexer"/> directamente y no <c>IDistributedCache</c>
/// porque esa abstracción solo expone Get/Set/Remove: <b>no tiene «set si no existe»</b>,
/// que es justo la primitiva que hace falta. Es un caso legítimo de bajar un nivel.
/// </para>
/// <para>
/// <b>Falla en abierto</b>, y ahora eso es barato de razonar: lo que se pierde es el
/// atajo, no la garantía. Con Redis caído las peticiones duplicadas llegan hasta SQL y
/// allí las arbitra la clave primaria de <c>ExecutedCommands</c> — más lento, igual de
/// correcto. Antes esta misma línea significaba «ejecuta sin garantía de idempotencia»,
/// que es una decisión muy distinta escondida en el mismo <c>catch</c>.
/// </para>
/// <para>
/// ⚠️ Un viaje por petición para entrar y otro para salir, y ninguno guarda cuerpos.
/// La versión anterior memorizaba aquí la respuesta HTTP, lo que además de costar un
/// tercer viaje creaba una <b>segunda fuente de verdad</b>: el replay re-serializaba con
/// otras opciones y salía equivalente pero no idéntico al vivo.
/// </para>
/// </remarks>
public sealed class RedisIdempotencyStore(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> cacheOptions,
    ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore
{
  private readonly string _prefix = cacheOptions.Value.InstanceName + "idem:";

  /// <summary>Suelta el marcador solo si sigue siendo del que lo puso.</summary>
  /// <remarks>
  /// Es el <c>release</c> canónico de un lock distribuido. Sin la comprobación, una
  /// petición cuyo marcador ya caducó borraría el de otra que sí está en vuelo, y la
  /// puerta dejaría pasar a un tercero.
  /// </remarks>
  private const string ReleaseScript = """
      if redis.call('GET', KEYS[1]) == ARGV[1] then
        return redis.call('DEL', KEYS[1])
      end
      return 0
      """;

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
      // Corre en el camino de salida, incluido el de error: si lanzara, sustituiría la
      // excepción real del servicio (el 404/409 que el cliente debe ver) por un fallo de
      // Redis. Lo peor que pasa si no se suelta es que el marcador caduque solo.
      logger.LogWarning(ex, "Could not release the idempotency gate for {Key}", key);
    }
  }
}
