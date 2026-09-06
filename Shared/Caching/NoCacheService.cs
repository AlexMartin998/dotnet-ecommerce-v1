namespace ApiEcommerce.Shared.Caching;


/// <summary>
/// Cache que no cachea. Se registra cuando <c>Redis:Configuration</c> está vacío.
/// </summary>
/// <remarks>
/// Null Object: los servicios decorados siguen pidiendo un <see cref="ICacheService"/> sin
/// necesitar un <c>if</c> ni una bandera repartida por el código. La decisión se toma una
/// vez, en el registro de DI.
/// </remarks>
public sealed class NoCacheService : ICacheService
{
  /// <inheritdoc />
  public Task<T> GetOrSetAsync<T>(
      string key, Func<CancellationToken, Task<T>> factory,
      TimeSpan? ttl = null, CancellationToken ct = default)
      => factory(ct);

  /// <inheritdoc />
  public Task RemoveAsync(CancellationToken ct = default, params string[] keys)
      => Task.CompletedTask;
}
