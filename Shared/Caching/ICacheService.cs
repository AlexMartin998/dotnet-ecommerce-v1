namespace ApiEcommerce.Shared.Caching;


/// <summary>
/// Cache de aplicación con patrón <b>cache-aside</b>: se pregunta a la cache y, si no
/// está, se calcula el valor, se guarda y se devuelve.
/// </summary>
/// <remarks>
/// <para>
/// Es una interfaz propia y no <c>IDistributedCache</c> directamente porque
/// <c>IDistributedCache</c> habla en <c>byte[]</c>: cada llamante tendría que
/// serializar a mano, y el "get, si null calcula y set" se repetiría en cada servicio.
/// </para>
/// <para>
/// Se prefiere esto a <c>[ResponseCache]</c> (lo que usa el curso de referencia) por
/// tres razones concretas: <c>ResponseCaching</c> vive en la memoria de <b>un</b>
/// proceso (no sirve con varias réplicas), <b>no cachea nada</b> si el request lleva
/// cabecera <c>Authorization</c>, y <b>no se puede invalidar</b> — una categoría
/// borrada se sigue sirviendo hasta que expire el TTL.
/// </para>
/// </remarks>
public interface ICacheService
{
  /// <summary>
  /// Devuelve el valor cacheado bajo <paramref name="key"/> o ejecuta
  /// <paramref name="factory"/>, guarda su resultado y lo devuelve.
  /// </summary>
  /// <param name="key">Clave lógica, sin el prefijo de instancia.</param>
  /// <param name="factory">Cómo obtener el valor cuando no está en cache.</param>
  /// <param name="ttl">Vida de la entrada. <c>null</c> = el TTL por defecto de configuración.</param>
  /// <param name="ct">Token de cancelación.</param>
  Task<T> GetOrSetAsync<T>(
      string key, Func<CancellationToken, Task<T>> factory,
      TimeSpan? ttl = null, CancellationToken ct = default);

  /// <summary>Invalida una o varias claves. Se llama tras cada escritura.</summary>
  Task RemoveAsync(CancellationToken ct = default, params string[] keys);
}
