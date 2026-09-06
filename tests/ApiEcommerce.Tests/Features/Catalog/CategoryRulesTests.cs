using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Features.Catalog.Service;
using Moq;

namespace ApiEcommerce.Tests.Features.Catalog;


/// <summary>Reglas de negocio de <c>Category</c>.</summary>
/// <remarks>
/// Las reglas viven fuera del CRUD, así que se instancian con un repositorio falso y sin
/// base, <c>IMapper</c> ni <c>HttpContext</c>.
/// </remarks>
public class CategoryRulesTests
{
  private readonly Mock<ICategoryRepository> _repository = new(MockBehavior.Strict);

  private CategoryRules Sut() => new(_repository.Object);

  // ---- crear --------------------------------------------------------------

  [Fact]
  public async Task EnsureCanCreateAsync_WhenNameIsTaken_ThrowsConflict()
  {
    // Arrange
    _repository.Setup(r => r.NameExistsAsync("Bebidas", null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

    // Act
    var ex = await Assert.ThrowsAsync<ConflictAppException>(
        () => Sut().EnsureCanCreateAsync(new CreateCategoryDto { Name = "Bebidas" }));

    // Assert — el 409 lo decide la excepción y no el controller, por eso se comprueba
    // el Status y no un IActionResult.
    Assert.Equal(HttpStatusCode.Conflict, ex.Status);
    Assert.Equal("conflict", ex.Code);
    Assert.Contains("Bebidas", ex.Message);
  }

  [Fact]
  public async Task EnsureCanCreateAsync_WhenNameIsFree_DoesNotThrow()
  {
    _repository.Setup(r => r.NameExistsAsync("Bebidas", null, It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

    await Sut().EnsureCanCreateAsync(new CreateCategoryDto { Name = "Bebidas" });

    _repository.VerifyAll();
  }

  // ---- actualizar ---------------------------------------------------------

  [Theory]
  [InlineData(null)]
  [InlineData("")]
  [InlineData("   ")]
  public async Task EnsureCanUpdateAsync_WhenNameIsNotSent_SkipsTheCheck(string? name)
  {
    // En un PATCH lo que no viene no se valida: con MockBehavior.Strict, consultar la base
    // por un campo que el cliente no mandó haría fallar el test.
    await Sut().EnsureCanUpdateAsync(1, new UpdateCategoryDto { Name = name }, Existing(1, "Bebidas"));

    _repository.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task EnsureCanUpdateAsync_ExcludesItsOwnId()
  {
    // Sin excludeId, renombrar una categoría a su propio nombre choca consigo misma.
    _repository.Setup(r => r.NameExistsAsync("Bebidas", 7, It.IsAny<CancellationToken>()))
               .ReturnsAsync(false);

    await Sut().EnsureCanUpdateAsync(7, new UpdateCategoryDto { Name = "Bebidas" }, Existing(7, "Bebidas"));

    _repository.Verify(r => r.NameExistsAsync("Bebidas", 7, It.IsAny<CancellationToken>()), Times.Once);
  }

  [Fact]
  public async Task EnsureCanUpdateAsync_WhenAnotherCategoryHasTheName_ThrowsConflict()
  {
    _repository.Setup(r => r.NameExistsAsync("Bebidas", 7, It.IsAny<CancellationToken>()))
               .ReturnsAsync(true);

    var ex = await Assert.ThrowsAsync<ConflictAppException>(
        () => Sut().EnsureCanUpdateAsync(7, new UpdateCategoryDto { Name = "Bebidas" }, Existing(7, "Snacks")));

    Assert.Equal(HttpStatusCode.Conflict, ex.Status);
  }

  // ---- borrar -------------------------------------------------------------

  [Fact]
  public async Task EnsureCanDeleteAsync_WhenCategoryStillHasProducts_ThrowsConflict()
  {
    _repository.Setup(r => r.HasProductsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(true);

    var ex = await Assert.ThrowsAsync<ConflictAppException>(
        () => Sut().EnsureCanDeleteAsync(Existing(7, "Bebidas")));

    // 409 y no 400: el request es válido en sí mismo, choca con el estado de la base.
    Assert.Equal(HttpStatusCode.Conflict, ex.Status);
    Assert.Contains("still has products", ex.Message);
  }

  [Fact]
  public async Task EnsureCanDeleteAsync_WhenCategoryIsEmpty_DoesNotThrow()
  {
    _repository.Setup(r => r.HasProductsAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(false);

    await Sut().EnsureCanDeleteAsync(Existing(7, "Bebidas"));

    _repository.VerifyAll();
  }

  [Fact]
  public void EntityName_IsTheDomainName()
  {
    // Alimenta el mensaje del 404, que es contrato de la API: no puede cambiar porque se
    // renombre la clase C#.
    Assert.Equal("Category", Sut().EntityName);
  }

  private static Category Existing(int id, string name) => new() { Id = id, Name = name };
}
