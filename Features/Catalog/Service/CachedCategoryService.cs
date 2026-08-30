using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// <b>Decorador</b> de <see cref="ICategoryService"/> que añade cache-aside a las
/// lecturas e invalidación a las escrituras.
/// </summary>
/// <remarks>
/// <para>
/// Este es el mismo principio que gobierna todo el proyecto —"se compone para
/// reutilizar política"— llevado a un aspecto transversal. <c>CategoryService</c>
/// <b>no sabe que existe una cache</b>: sigue siendo el servicio de negocio puro y se
/// puede testear sin Redis. Quitar la cache es borrar una línea del registro de DI.
/// </para>
/// <para>
/// Es el equivalente a <c>@Cacheable</c> / <c>@CacheEvict</c> de Spring, pero
/// explícito: se ve qué clave se lee y qué claves se tiran, en vez de deducirlo de
/// una anotación y una convención de nombres.
/// </para>
/// <para>
/// <b>Solo se cachean las lecturas de catálogo</b>, que son públicas
/// (<c>[AllowAnonymous]</c>) e iguales para todos. Nada que dependa del usuario
/// autenticado entra aquí: una cache compartida con datos por-usuario es una fuga de
/// datos entre cuentas.
/// </para>
/// </remarks>
public sealed class CachedCategoryService(ICategoryService inner, ICacheService cache) : ICategoryService
{
  /// <summary>TTL corto: el catálogo cambia poco, pero la ventana de datos rancios tampoco debe ser grande.</summary>
  private static readonly TimeSpan Ttl = TimeSpan.FromSeconds(60);

  // ---- lecturas: cache-aside ----------------------------------------------

  public Task<IEnumerable<CategoryDto>> GetAllAsync(CancellationToken ct = default)
      => cache.GetOrSetAsync(CacheKeys.CategoryAll, inner.GetAllAsync, Ttl, ct);

  public Task<CategoryDto> GetByIdAsync(int id, CancellationToken ct = default)
      => cache.GetOrSetAsync(CacheKeys.Category(id), token => inner.GetByIdAsync(id, token), Ttl, ct);

  // Las páginas NO se cachean: cada combinación de page/pageSize sería una clave
  // distinta que ninguna invalidación conoce. Cachear listados paginados exige
  // invalidar por prefijo (o versionar la clave de colección), y eso ya no es una
  // línea de decorador: es una decisión aparte, con su propio coste.
  public Task<PagedResult<CategoryDto>> GetPagedAsync(PageQuery query, CancellationToken ct = default)
      => inner.GetPagedAsync(query, ct);

  // ---- escrituras: pasan de largo e invalidan ------------------------------
  // La invalidación va DESPUÉS de que la escritura haya ido bien: si el servicio
  // lanza (409 por nombre duplicado, 404...), no se tira una cache que sigue siendo
  // válida.

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
