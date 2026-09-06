using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Features.Catalog.Service;
using Moq;

namespace ApiEcommerce.Tests.Features.Catalog;


/// <summary>
/// Reglas de negocio de <c>Product</c>: SKU único y categoría existente.
/// </summary>
public class ProductRulesTests
{
  private readonly Mock<IProductRepository> _products = new(MockBehavior.Strict);
  private readonly Mock<ICategoryRepository> _categories = new(MockBehavior.Strict);

  private ProductRules Sut() => new(_products.Object, _categories.Object);

  // ---- crear --------------------------------------------------------------

  [Fact]
  public async Task EnsureCanCreateAsync_WhenCategoryDoesNotExist_ThrowsBadRequest()
  {
    _categories.Setup(r => r.ExistsAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(false);

    var ex = await Assert.ThrowsAsync<BadOperationAppException>(
        () => Sut().EnsureCanCreateAsync(Create(sku: "SKU-1", categoryId: 99)));

    // 400 y no 404: el recurso pedido es el producto, y la categoría inexistente hace el
    // request inválido. Sin validarlo aquí, EF revienta por FK y sale un 500.
    Assert.Equal(HttpStatusCode.BadRequest, ex.Status);
    Assert.Equal("bad_request", ex.Code);
  }

  [Fact]
  public async Task EnsureCanCreateAsync_ChecksTheCategoryBeforeTheSku()
  {
    // Con categoría inexistente y SKU duplicado, lo primero que hay que arreglar es la
    // categoría, y el mensaje tiene que decir eso.
    _categories.Setup(r => r.ExistsAsync(99, It.IsAny<CancellationToken>())).ReturnsAsync(false);

    await Assert.ThrowsAsync<BadOperationAppException>(
        () => Sut().EnsureCanCreateAsync(Create(sku: "SKU-1", categoryId: 99)));

    // Con MockBehavior.Strict, si hubiera consultado el SKU el test ya habría fallado.
    _products.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task EnsureCanCreateAsync_WhenSkuIsTaken_ThrowsConflict()
  {
    _categories.Setup(r => r.ExistsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    _products.Setup(r => r.SkuExistsAsync("SKU-1", null, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    var ex = await Assert.ThrowsAsync<ConflictAppException>(
        () => Sut().EnsureCanCreateAsync(Create(sku: "SKU-1", categoryId: 1)));

    Assert.Equal(HttpStatusCode.Conflict, ex.Status);
    Assert.Contains("SKU-1", ex.Message);
  }

  [Fact]
  public async Task EnsureCanCreateAsync_WhenEverythingIsValid_DoesNotThrow()
  {
    _categories.Setup(r => r.ExistsAsync(1, It.IsAny<CancellationToken>())).ReturnsAsync(true);
    _products.Setup(r => r.SkuExistsAsync("SKU-1", null, It.IsAny<CancellationToken>())).ReturnsAsync(false);

    await Sut().EnsureCanCreateAsync(Create(sku: "SKU-1", categoryId: 1));

    _categories.VerifyAll();
    _products.VerifyAll();
  }

  // ---- actualizar (PATCH: solo se valida lo que llega) ---------------------

  [Fact]
  public async Task EnsureCanUpdateAsync_WhenNothingIsSent_ChecksNothing()
  {
    await Sut().EnsureCanUpdateAsync(5, new UpdateProductDto(), Existing(5));

    _categories.VerifyNoOtherCalls();
    _products.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task EnsureCanUpdateAsync_WhenOnlySkuIsSent_ChecksOnlyTheSku()
  {
    _products.Setup(r => r.SkuExistsAsync("SKU-2", 5, It.IsAny<CancellationToken>())).ReturnsAsync(false);

    await Sut().EnsureCanUpdateAsync(5, new UpdateProductDto { SKU = "SKU-2" }, Existing(5));

    _categories.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task EnsureCanUpdateAsync_ExcludesItsOwnId()
  {
    // Sin excludeId, reenviar el mismo SKU en un PATCH choca consigo mismo.
    _products.Setup(r => r.SkuExistsAsync("SKU-1", 5, It.IsAny<CancellationToken>())).ReturnsAsync(false);

    await Sut().EnsureCanUpdateAsync(5, new UpdateProductDto { SKU = "SKU-1" }, Existing(5));

    _products.Verify(r => r.SkuExistsAsync("SKU-1", 5, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task EnsureCanUpdateAsync_WhenCategoryIdIsSentAndDoesNotExist_ThrowsBadRequest()
  {
    // Un 0 se valida: es un id inválido, no una ausencia (eso es null).
    _categories.Setup(r => r.ExistsAsync(0, It.IsAny<CancellationToken>())).ReturnsAsync(false);

    await Assert.ThrowsAsync<BadOperationAppException>(
        () => Sut().EnsureCanUpdateAsync(5, new UpdateProductDto { CategoryId = 0 }, Existing(5)));
  }

  [Fact]
  public void EntityName_IsTheDomainName() => Assert.Equal("Product", Sut().EntityName);

  private static CreateProductDto Create(string sku, int categoryId) =>
      new() { Name = "Producto", SKU = sku, CategoryId = categoryId, Price = 10m, Stock = 5 };

  private static Product Existing(int id) =>
      new() { Id = id, Name = "Producto", SKU = "SKU-1", CategoryId = 1, Price = 10m, Stock = 5 };
}
