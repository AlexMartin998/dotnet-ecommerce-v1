namespace ApiEcommerce.Shared.Caching;


/// <summary>
/// Cache que no cachea. Se registra cuando <c>Redis:Configuration</c> está vacío.
/// </summary>
/// <remarks>
/// Es el patrón <b>Null Object</b>: los servicios decorados siguen pidiendo un
/// <see cref="ICacheService"/> y no necesitan un <c>if (cache is not null)</c> ni una
/// bandera de configuración repartida por el código. La decisión "hay cache o no"
/// se toma <b>una vez</b>, en el registro de DI.
/// </remarks>
public sealed class NoCacheService : ICacheService
{
  public Task<T> GetOrSetAsync<T>(
      string key, Func<CancellationToken, Task<T>> factory,
      TimeSpan? ttl = null, CancellationToken ct = default)
      => factory(ct);

  public Task RemoveAsync(CancellationToken ct = default, params string[] keys)
      => Task.CompletedTask;
}
