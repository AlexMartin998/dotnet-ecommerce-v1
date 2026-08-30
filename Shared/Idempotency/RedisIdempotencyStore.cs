using System.Text.Json;
using Microsoft.Extensions.Options;
using ApiEcommerce.Shared.Caching;
using StackExchange.Redis;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Implementación sobre Redis usando <c>SET ... NX</c> para la reserva atómica.
/// </summary>
/// <remarks>
/// <para>
/// Usa <see cref="IConnectionMultiplexer"/> directamente y no <c>IDistributedCache</c>
/// porque esa abstracción solo expone Get/Set/Remove: <b>no tiene "set si no existe"</b>,
/// que es justo la primitiva que hace falta. Es un caso legítimo de bajar un nivel.
/// </para>
/// <para>
/// <b>Falla en abierto</b>, igual que <c>RedisCacheService</c> y que
/// <c>NoIdempotencyStore</c>. Con Redis caído se pierde la protección contra el doble
/// submit, pero la API sigue funcionando. Sin esto, un corte de Redis <b>justo después
/// del commit</b> hacía que <c>SaveAsync</c> lanzara desde el filtro y el cliente
/// recibiera un 500 por una compra que SÍ se había cobrado — y al reintentar se
/// encontraba la clave reservada. El mecanismo que existe para evitar el doble cobro
/// era el que lo provocaba.
/// </para>
/// <para>
/// Si algún día la idempotencia fuera un requisito duro (pagos), habría que invertir
/// esta decisión <b>y</b> la de <c>NoIdempotencyStore</c> a la vez, y devolver 503 en
/// vez de ejecutar sin garantía.
/// </para>
/// </remarks>
public sealed class RedisIdempotencyStore(
    IConnectionMultiplexer redis,
    IOptions<CacheOptions> cacheOptions,
    ILogger<RedisIdempotencyStore> logger) : IIdempotencyStore
{
  private readonly string _prefix = cacheOptions.Value.InstanceName + "idem:";

  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

  /// <summary>Marca de "en curso": la clave está reservada pero aún no hay respuesta.</summary>
  private const string InProgress = "__in_progress__";

  public async Task<bool> TryAcquireAsync(string key, TimeSpan ttl, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    try
    {
      return await redis.GetDatabase().StringSetAsync(
          _prefix + key, InProgress, ttl, When.NotExists);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Degrada a "eres el primero": se ejecuta sin garantía de idempotencia, que es
      // exactamente lo que hace NoIdempotencyStore cuando no hay Redis configurado.
      logger.LogWarning(ex, "Idempotency store unavailable on acquire for {Key}; proceeding without guarantee", key);
      return true;
    }
  }

  public async Task<IdempotentResponse?> GetAsync(string key, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    RedisValue value;

    try
    {
      value = await redis.GetDatabase().StringGetAsync(_prefix + key);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      logger.LogWarning(ex, "Idempotency store unavailable on read for {Key}", key);
      return null;
    }

    if (!value.HasValue) return null;

    // Reservada pero todavía ejecutándose: no hay nada que reproducir.
    if (value == InProgress) return null;

    try
    {
      return JsonSerializer.Deserialize<IdempotentResponse>(value!, SerializerOptions);
    }
    catch (JsonException ex)
    {
      logger.LogWarning(ex, "Corrupt idempotency entry for {Key}; treating as absent", key);
      return null;
    }
  }

  public async Task SaveAsync(
      string key, IdempotentResponse response, TimeSpan ttl, CancellationToken ct = default)
  {
    try
    {
      await redis.GetDatabase().StringSetAsync(
          _prefix + key, JsonSerializer.Serialize(response, SerializerOptions), ttl);
    }
    catch (Exception ex)
    {
      // NUNCA puede cambiar el resultado de una operación ya confirmada en base de
      // datos. Como mucho se pierde la memoria del reintento.
      logger.LogWarning(ex, "Could not memorise idempotent response for {Key}", key);
    }
  }

  public async Task ReleaseAsync(string key, CancellationToken ct = default)
  {
    try
    {
      await redis.GetDatabase().KeyDeleteAsync(_prefix + key);
    }
    catch (Exception ex)
    {
      // Corre en el camino de ERROR: si lanzara, sustituiría la excepción real del
      // servicio (el 404/409 que el cliente debe ver) por un fallo de Redis.
      logger.LogWarning(ex, "Could not release idempotency key {Key}", key);
    }
  }
}
