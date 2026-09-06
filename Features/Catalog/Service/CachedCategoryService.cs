using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Decorador de <see cref="ICategoryService"/> que añade cache-aside a las lecturas e
/// invalidación a las escrituras.
/// </summary>
/// <remarks>
/// Solo se cachean lecturas públicas e iguales para todos: una cache compartida con
/// datos por-usuario sería una fuga entre cuentas.
/// </remarks>
public sealed class CachedCategoryService(ICategoryService inner, ICacheService cache) : ICategoryService
{
  /// <summary>TTL corto, para acotar la ventana de datos rancios.</summary>
  private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

  // ---- lecturas: cache-aside ----------------------------------------------

  public Task<IEnumerable<CategoryDto>> GetAllAsync(CancellationToken ct = default)
      => cache.GetOrSetAsync(CacheKeys.CategoryAll, inner.GetAllAsync, Ttl, ct);

  public Task<CategoryDto> GetByIdAsync(int id, CancellationToken ct = default)
      => cache.GetOrSetAsync(CacheKeys.Category(id), token => inner.GetByIdAsync(id, token), Ttl, ct);

  // Las páginas no se cachean: cada combinación de page/pageSize sería una clave que
  // ninguna invalidación conoce, y eso exigiría invalidar por prefijo.
  public Task<PagedResult<CategoryDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default)
      => inner.GetPagedAsync(query, ct);

  // ---- escrituras: pasan de largo e invalidan ------------------------------
  // La invalidación va después de que la escritura haya ido bien: si el servicio lanza,
  // no se tira una cache que sigue siendo válida.

  public async Task<int> CreateAsync(CreateCategoryDto dto, CancellationToken ct = default)
  {
    var id = await inner.CreateAsync(dto, ct);
    await cache.RemoveAsync(ct, CacheKeys.CategoryAll);
    return id;
  }

  public async Task UpdateAsync(int id, UpdateCategoryDto dto, CancellationToken ct = default)
  {
    await inner.UpdateAsync(id, dto, ct);
    await cache.RemoveAsync(ct, CacheKeys.CategoryAll, CacheKeys.Category(id));
  }

  public async Task DeleteAsync(int id, CancellationToken ct = default)
  {
    await inner.DeleteAsync(id, ct);
    await cache.RemoveAsync(ct, CacheKeys.CategoryAll, CacheKeys.Category(id));
  }
}
