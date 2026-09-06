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

  /// <summary>
  /// Sólo escribe si la clave está libre o si la reserva sigue siendo <b>nuestra</b>.
  /// </summary>
  /// <remarks>
  /// El dueño va como prefijo del valor, así que comparar es cortar los primeros N bytes.
  /// La alternativa —meterlo dentro del JSON y buscarlo con <c>string.find</c>— no es
  /// exacta: el cuerpo memorizado podría contener esa misma subcadena.
  /// </remarks>
  private const string SaveScript = """
      local v = redis.call('GET', KEYS[1])
      if v and string.sub(v, 1, #ARGV[1]) ~= ARGV[1] then return 0 end
      redis.call('SET', KEYS[1], ARGV[2], 'EX', ARGV[3])
      return 1
      """;

  /// <summary>Sólo borra si la reserva sigue siendo nuestra.</summary>
  private const string ReleaseScript = """
      local v = redis.call('GET', KEYS[1])
      if v and string.sub(v, 1, #ARGV[1]) == ARGV[1] then
        return redis.call('DEL', KEYS[1])
      end
      return 0
      """;

  public async Task<IdempotencyAcquisition> TryAcquireAsync(
      string key, string requestHash, TimeSpan ttl, CancellationToken ct = default)
  {
    ct.ThrowIfCancellationRequested();

    // Token de propiedad de ESTA reserva. Sin él, `Release` y `Save` son incondicionales
    // y una petición cuya reserva ya caducó puede pisar la de otra:
    //
    //   A reserva (60 s) -> la acción de A se eterniza -> la reserva caduca ->
    //   B reserva y ejecuta -> A termina en error y hace Release -> BORRA la reserva
    //   VIVA de B -> un tercer reintento vuelve a pasar el SET NX y ejecuta otra vez.
    //
    // Sin dueño, la ventana de duplicación deja de estar acotada por el TTL: se
    // reabre en cada vuelta. Con dueño, A no puede tocar nada que ya no sea suyo.
    var fence = Guid.NewGuid().ToString("N");

    RedisValue previous;

    try
    {
      // SET clave valor EX ttl NX GET: reserva si no existe y, si existía, devuelve lo
      // que había. Las dos preguntas que hace el filtro en UN viaje.
      //
      // ⚠️ Exige Redis >= 7.0: hasta la 6.2, combinar NX con GET era un error de
      // sintaxis. Si algún día esto corriera contra una versión anterior, el catch de
      // abajo lo tomaría por "Redis no responde" y la idempotencia se apagaría en
      // silencio. Por eso el mensaje del log nombra la operación completa.
      previous = await redis.GetDatabase().StringSetAndGetAsync(
          _prefix + key,
          Wrap(fence, new IdempotencyEntry(requestHash, null)),
          ttl, keepTtl: false, when: When.NotExists);
    }
    catch (Exception ex) when (ex is not OperationCanceledException)
    {
      // Degrada en abierto: se ejecuta sin garantía de idempotencia, que es lo mismo
      // que hace NoIdempotencyStore cuando no hay Redis configurado.
      //
      // ⚠️ Bajo carga esto NO es hipotético: con un solo multiplexer y un timeout de
      // 1000 ms, una ráfaga basta para que el SET expire con Redis perfectamente sano.
      // Medido: 26 de 1000 peticiones a 64 conexiones. Por eso el resultado es
      // `Unavailable` y no `Acquired`: quien llama tiene que poder contarlo y avisar.
      logger.LogWarning(ex,
          "Idempotency store unavailable on acquire (SET NX GET) for {Key}; proceeding without guarantee", key);

      return IdempotencyAcquisition.Unavailable;
    }

    // Sin valor previo = la clave era nuestra. Ejecutar.
    if (!previous.HasValue) return IdempotencyAcquisition.Acquired(fence);

    var entry = Unwrap(previous!, key);

    // Entrada ilegible: la clave está ocupada pero no se puede ni comparar ni
    // reproducir. Se devuelve como una reserva anónima —huella vacía, sin respuesta—,
    // que el filtro traduce en 409 hasta que el TTL la limpie. Ejecutar sería peor: es
    // justo el caso en el que no se sabe si la operación ya corrió.
    return IdempotencyAcquisition.Existing(entry ?? new IdempotencyEntry(string.Empty, null));
  }

  public async Task SaveAsync(
      string key, string fence, string requestHash, IdempotentResponse response, TimeSpan ttl,
      CancellationToken ct = default)
  {
    try
    {
      var db = redis.GetDatabase();
      var value = Wrap(fence, new IdempotencyEntry(requestHash, response));

      if (string.IsNullOrEmpty(fence))
      {
        // Camino degradado: nunca llegamos a reservar, así que no somos dueños de nada.
        // Se memoriza sólo si la clave está LIBRE — pisar la entrada de otro sería
        // peor que perder la memoria de esta respuesta.
        await db.StringSetAsync(_prefix + key, value, ttl, When.NotExists);
        return;
      }

      await db.ScriptEvaluateAsync(
          SaveScript,
          [_prefix + key],
          [fence, value, (long)ttl.TotalSeconds]);
    }
    catch (Exception ex)
    {
      // NUNCA puede cambiar el resultado de una operación ya confirmada en base de
      // datos. Como mucho se pierde la memoria del reintento.
      //
      // ⚠️ Lo que se pierde no es poco: sin respuesta memorizada, el cliente recibe 409
      // hasta que caduque la reserva y DESPUÉS su reintento vuelve a ejecutar de verdad.
      // Está anotado en planning/16 §16.6; arreglarlo pide guardar la marca en la misma
      // transacción que el efecto, o sea en SQL y no en Redis.
      logger.LogWarning(ex, "Could not memorise idempotent response for {Key}", key);
    }
  }

  public async Task ReleaseAsync(string key, string fence, CancellationToken ct = default)
  {
    // Sin token no reservamos nada: no hay nada nuestro que liberar. Un DEL aquí
    // borraría la reserva de otro.
    if (string.IsNullOrEmpty(fence)) return;

    try
    {
      await redis.GetDatabase().ScriptEvaluateAsync(ReleaseScript, [_prefix + key], [fence]);
    }
    catch (Exception ex)
    {
      // Corre en el camino de ERROR: si lanzara, sustituiría la excepción real del
      // servicio (el 404/409 que el cliente debe ver) por un fallo de Redis.
      logger.LogWarning(ex, "Could not release idempotency key {Key}", key);
    }
  }

  /// <summary>Antepone el token de propiedad al JSON de la entrada.</summary>
  private static string Wrap(string fence, IdempotencyEntry entry)
      => fence + FenceSeparator + JsonSerializer.Serialize(entry, SerializerOptions);

  /// <summary>Quita el token y deserializa; <c>null</c> si la entrada no es legible.</summary>
  private IdempotencyEntry? Unwrap(string stored, string key)
  {
    // Las entradas escritas antes de que existiera el token no llevan prefijo. Se leen
    // igual —empiezan por '{'— y caducan solas en 24 h.
    var separator = stored.IndexOf(FenceSeparator);
    var json = separator >= 0 ? stored[(separator + 1)..] : stored;

    try
    {
      return JsonSerializer.Deserialize<IdempotencyEntry>(json, SerializerOptions);
    }
    catch (JsonException ex)
    {
      logger.LogWarning(ex, "Corrupt idempotency entry for {Key}", key);
      return null;
    }
  }

  private const char FenceSeparator = '|';
}
