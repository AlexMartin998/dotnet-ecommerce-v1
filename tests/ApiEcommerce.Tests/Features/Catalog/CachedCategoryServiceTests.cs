using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Service;
using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Paging;
using Moq;

namespace ApiEcommerce.Tests.Features.Catalog;


/// <summary>
/// El decorador de cache. Lo que hay que fijar no es "cachea", es <b>cuándo invalida</b>:
/// ahí es donde viven los bugs de cache que se manifiestan como datos rancios eternos.
/// </summary>
public class CachedCategoryServiceTests
{
  private readonly Mock<ICategoryService> _inner = new();
  private readonly Mock<ICacheService> _cache = new();

  private CachedCategoryService Sut() => new(_inner.Object, _cache.Object);

  // ---- lecturas -----------------------------------------------------------

  [Fact]
  public async Task GetAllAsync_ReadsThroughTheCacheWithTheCollectionKey()
  {
    _cache.Setup(c => c.GetOrSetAsync(
              CacheKeys.CategoryAll, It.IsAny<Func<CancellationToken, Task<IEnumerable<CategoryDto>>>>(),
              It.IsAny<TimeSpan?>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync([]);

    await Sut().GetAllAsync();

    // La clave que se LEE tiene que ser exactamente la que luego se invalida. Que
    // ambas salgan de CacheKeys es lo que lo garantiza; el test lo fija.
    _cache.VerifyAll();
  }

  [Fact]
  public async Task GetPagedAsync_DoesNotTouchTheCache()
  {
    // Decisión explícita: cada combinación page/pageSize sería una clave que ninguna
    // invalidación conoce. Si alguien "mejora" esto cacheando páginas, este test cae.
    _inner.Setup(s => s.GetPagedAsync(It.IsAny<PageQuery>(), It.IsAny<CancellationToken>()))
          .ReturnsAsync(PagedResult<CategoryDto>.Empty(1, 10));

    await Sut().GetPagedAsync(new PageQuery());

    _cache.VerifyNoOtherCalls();
  }

  // ---- escrituras ---------------------------------------------------------

  [Fact]
  public async Task CreateAsync_InvalidatesAfterTheWriteSucceeds()
  {
    var order = new List<string>();
    _inner.Setup(s => s.CreateAsync(It.IsAny<CreateCategoryDto>(), It.IsAny<CancellationToken>()))
          .Callback(() => order.Add("inner")).ReturnsAsync(42);
    _cache.Setup(c => c.RemoveAsync(It.IsAny<CancellationToken>(), CacheKeys.CategoryAll))
          .Callback(() => order.Add("invalidate")).Returns(Task.CompletedTask);

    var id = await Sut().CreateAsync(new CreateCategoryDto { Name = "Bebidas" });

    Assert.Equal(["inner", "invalidate"], order);
    Assert.Equal(42, id);
  }

  [Fact]
  public async Task WhenTheInnerServiceThrows_TheCacheIsNotInvalidated()
  {
    // ⚠️ El orden importa en las dos direcciones. Invalidar ANTES de escribir tira una
    // cache que sigue siendo válida cuando la escritura falla (409 por duplicado, 404…),
    // y encima repuebla con los datos viejos en la siguiente lectura.
    _inner.Setup(s => s.CreateAsync(It.IsAny<CreateCategoryDto>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConflictAppException("ya existe"));

    await Assert.ThrowsAsync<ConflictAppException>(
        () => Sut().CreateAsync(new CreateCategoryDto { Name = "Bebidas" }));

    _cache.Verify(c => c.RemoveAsync(It.IsAny<CancellationToken>(), It.IsAny<string[]>()), Times.Never);
  }

  [Fact]
  public async Task UpdateAsync_InvalidatesBothTheItemAndTheCollection()
  {
    // Solo la clave del item deja el listado sirviendo el nombre viejo; solo la del
    // listado deja el GET por id rancio. Hay que tirar las dos.
    await Sut().UpdateAsync(7, new UpdateCategoryDto { Name = "Snacks" });

    _cache.Verify(c => c.RemoveAsync(
        It.IsAny<CancellationToken>(),
        It.Is<string[]>(k => k.Contains(CacheKeys.CategoryAll) && k.Contains(CacheKeys.Category(7)))),
        Times.Once);
  }

  [Fact]
  public async Task DeleteAsync_InvalidatesBothTheItemAndTheCollection()
  {
    await Sut().DeleteAsync(7);

    _cache.Verify(c => c.RemoveAsync(
        It.IsAny<CancellationToken>(),
        It.Is<string[]>(k => k.Contains(CacheKeys.CategoryAll) && k.Contains(CacheKeys.Category(7)))),
        Times.Once);
  }
}
