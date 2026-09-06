using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Features.Catalog.Service;
using Moq;

namespace ApiEcommerce.Tests.Features.Catalog;


/// <summary>
/// Reglas de negocio de <c>Category</c>.
/// </summary>
/// <remarks>
/// Estos tests son la prueba de que la composición abarató el testeo: las reglas viven
/// fuera del CRUD, así que se instancian con un repositorio falso <b>en una línea</b> y
/// sin base de datos, sin <c>IMapper</c> y sin <c>HttpContext</c>. Con los hooks
/// <c>virtual</c> de una clase base habría que levantar el servicio entero.
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

    // Assert — el 409 lo decide la excepción, no el controller: quien la traduce a HTTP
    // es GlobalExceptionHandler, y por eso aquí se comprueba el Status y no un IActionResult.
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
    // En un PATCH lo que no viene no se valida. Con MockBehavior.Strict, cualquier
    // llamada al repositorio haría fallar el test: eso es justo lo que se quiere
    // comprobar — que no se consulta la base para un campo que el cliente no mandó.
    await Sut().EnsureCanUpdateAsync(1, new UpdateCategoryDto { Name = name }, Existing(1, "Bebidas"));

    _repository.VerifyNoOtherCalls();
  }

  [Fact]
  public async Task EnsureCanUpdateAsync_ExcludesItsOwnId()
  {
    // ⚠️ La regla que más fácil se rompe: sin excludeId, renombrar una categoría a su
    // propio nombre choca CONSIGO MISMA y devuelve un 409 absurdo.
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

    // 409 y no 400: el request es válido en sí mismo, choca con el ESTADO de la base.
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
    // Alimenta el mensaje del 404 ("Category with key '9' was not found"), que es
    // contrato de la API: no puede pasar a llamarse como la clase C# si esta se renombra.
    Assert.Equal("Category", Sut().EntityName);
  }

  private static Category Existing(int id, string name) => new() { Id = id, Name = name };
}
