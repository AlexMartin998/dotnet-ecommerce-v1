namespace ApiEcommerce.Shared.Caching;


/// <summary>
/// Cache de aplicación con patrón cache-aside: se pregunta a la cache y, si no está, se
/// calcula el valor, se guarda y se devuelve.
/// </summary>
/// <remarks>
/// Es interfaz propia y no <c>IDistributedCache</c>, que habla en <c>byte[]</c> y repetiría
/// el «get, si null calcula y set» en cada servicio. Se prefiere a <c>[ResponseCache]</c>,
/// que es por proceso, no cachea con <c>Authorization</c> y no se puede invalidar.
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
