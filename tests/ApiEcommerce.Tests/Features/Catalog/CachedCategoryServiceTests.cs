using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Service;
using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Paging;
using Moq;

namespace ApiEcommerce.Tests.Features.Catalog;


/// <summary>
/// El decorador de cache. Lo que se fija no es que cachee sino cuándo invalida, que es
/// donde viven los datos rancios eternos.
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

    // La clave que se lee tiene que ser exactamente la que luego se invalida.
    _cache.VerifyAll();
  }

  [Fact]
  public async Task GetPagedAsync_DoesNotTouchTheCache()
  {
    // Cada combinación page/pageSize sería una clave que ninguna invalidación conoce.
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
    // Invalidar antes de escribir tira una cache que sigue siendo válida cuando la
    // escritura falla, y la repuebla con los datos viejos en la siguiente lectura.
    _inner.Setup(s => s.CreateAsync(It.IsAny<CreateCategoryDto>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConflictAppException("ya existe"));

    await Assert.ThrowsAsync<ConflictAppException>(
        () => Sut().CreateAsync(new CreateCategoryDto { Name = "Bebidas" }));

    _cache.Verify(c => c.RemoveAsync(It.IsAny<CancellationToken>(), It.IsAny<string[]>()), Times.Never);
  }

  [Fact]
  public async Task UpdateAsync_InvalidatesBothTheItemAndTheCollection()
  {
    // Solo el item deja el listado con el nombre viejo; solo el listado deja rancio el
    // GET por id.
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
