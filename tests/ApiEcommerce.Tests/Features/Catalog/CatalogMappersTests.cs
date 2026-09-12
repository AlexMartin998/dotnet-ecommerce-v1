using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Mapping;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Tests.Features.Catalog;


/// <summary>
/// Mapeadores del catálogo. El grueso de los tests está en el PATCH parcial, que es lo
/// más fácil de romper sin que nada avise.
/// </summary>
/// <remarks>
/// No hay test de "la configuración es válida": un miembro del destino sin alimentar es
/// un RMG020 del generador, o sea un error de build con <c>-warnaserror</c>.
/// </remarks>
public class CatalogMappersTests
{
  private readonly ProductMapper _products = new();
  private readonly CategoryMapper _categories = new();

  // ---- PATCH parcial: lo que no viene, no se toca -------------------------

  [Fact]
  public void UpdateProduct_WithOnlyOneField_LeavesEverythingElseIntact()
  {
    // Un mapeo por convención que decide sobre el valor ya convertido al destino recibe
    // un `int?` nulo como 0, y un CategoryId en 0 rompe la FK con un 500 sin relación
    // aparente. Por eso el PATCH se expresa campo a campo.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto { Name = "Nombre nuevo" }, existing);

    Assert.Equal("Nombre nuevo", existing.Name);
    Assert.Equal(3, existing.CategoryId);      // <- el que reventaba
    Assert.Equal(10, existing.Stock);
    Assert.Equal(99.9m, existing.Price);
    Assert.Equal("SKU-1", existing.SKU);
  }

  [Fact]
  public void UpdateProduct_WithAnEmptyDto_ChangesNothing()
  {
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto(), existing);

    Assert.Equal("Producto", existing.Name);
    Assert.Equal(3, existing.CategoryId);
    Assert.Equal(10, existing.Stock);
    Assert.Equal(99.9m, existing.Price);
    Assert.Equal("/img/foto.png", existing.ImageUrl);
  }

  [Fact]
  public void UpdateProduct_CanSetAZeroOnPurpose()
  {
    // "No viene" es null y no 0: un agotamiento de stock legítimo tiene que aplicarse.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto { Stock = 0 }, existing);

    Assert.Equal(0, existing.Stock);
  }

  [Fact]
  public void UpdateProduct_ClearsAnOptionalFieldWithAnEmptyString()
  {
    // Semántica del PATCH: null = "no tocar", "" = "vaciar". Sin distinguirlos no habría
    // forma de borrar una descripción.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto { Description = "" }, existing);

    Assert.Equal("", existing.Description);
  }

  [Fact]
  public void UpdateCategory_WithOnlyTheDescription_KeepsTheName()
  {
    var existing = new Category { Id = 1, Name = "Bebidas", Description = "vieja" };

    _categories.Apply(new UpdateCategoryDto { Description = "nueva" }, existing);

    Assert.Equal("Bebidas", existing.Name);
    Assert.Equal("nueva", existing.Description);
  }

  // ---- auditoría: la estampa la base, no el mapper ------------------------

  [Fact]
  public void WritingNeverTouchesTheAuditFields()
  {
    // Los estampa AppDbContext: si el mapeador los tocara, un PATCH pisaría CreatedAt con
    // el default de un DTO que ni siquiera tiene esa propiedad.
    var created = new DateTime(2020, 1, 1);
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);
    existing.CreatedAt = created;
    existing.UpdatedAt = null;

    _products.Apply(new UpdateProductDto { Name = "otro" }, existing);

    Assert.Equal(created, existing.CreatedAt);
    Assert.Null(existing.UpdatedAt);
  }

  [Fact]
  public void CreateProduct_DoesNotCarryAnIdOrTheNavigation()
  {
    var entity = _products.ToEntity(new CreateProductDto
    {
      Name = "Nuevo", SKU = "SKU-9", Price = 1m, Stock = 1, CategoryId = 3
    });

    Assert.Equal(0, entity.Id);        // lo genera la base
    Assert.Null(entity.Category);      // se trabaja solo con CategoryId
    Assert.Equal(3, entity.CategoryId);
  }

  // ---- lectura ------------------------------------------------------------

  [Fact]
  public void ProductToDto_FlattensTheCategoryName()
  {
    var entity = Product(categoryId: 3, stock: 10, price: 99.9m);
    entity.Category = new Category { Id = 3, Name = "Bebidas" };

    Assert.Equal("Bebidas", _products.ToDto(entity).CategoryName);
  }

  [Fact]
  public void ProductToDto_WithoutTheIncludedNavigation_LeavesTheNameNullAndDoesNotThrow()
  {
    // Es lo que pasa si una consulta olvida el .Include: el mapeo no puede reventar, sale
    // null y se ve en la respuesta.
    Assert.Null(_products.ToDto(Product(categoryId: 3, stock: 10, price: 99.9m)).CategoryName);
  }

  // ---- normalización: se recorta al escribir -------------------------------

  [Fact]
  public void CreateProduct_TrimsTheNameAndTheSku()
  {
    // Sin normalizar, " SKU-1" y "SKU-1" conviven pese al índice único, y compararlos
    // obliga a envolver la columna en TRIM(), que anula ese mismo índice.
    var entity = _products.ToEntity(new CreateProductDto
    {
      Name = "  Nuevo  ", SKU = " SKU-9 ", Price = 1m, Stock = 1, CategoryId = 3
    });

    Assert.Equal("Nuevo", entity.Name);
    Assert.Equal("SKU-9", entity.SKU);
  }

  [Fact]
  public void UpdateProduct_TrimsTheNameAndTheSku()
  {
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto { Name = "  Otro  ", SKU = " SKU-2 " }, existing);

    Assert.Equal("Otro", existing.Name);
    Assert.Equal("SKU-2", existing.SKU);
  }

  [Fact]
  public void CreateCategory_TrimsTheName()
  {
    Assert.Equal("Bebidas", _categories.ToEntity(new CreateCategoryDto { Name = " Bebidas " }).Name);
  }

  // ---- el rowversion: binario en la base, texto en la cabecera -------------

  [Fact]
  public void ProductToDto_EncodesTheRowVersionAsBase64()
  {
    var entity = Product(categoryId: 3, stock: 10, price: 99.9m);
    entity.RowVersion = [1, 2, 3];

    Assert.Equal(Convert.ToBase64String([1, 2, 3]), _products.ToDto(entity).RowVersion);
  }

  [Fact]
  public void ProductToDto_WithoutARowVersion_LeavesItNull()
  {
    Assert.Null(_products.ToDto(Product(categoryId: 3, stock: 10, price: 99.9m)).RowVersion);
  }

  private static Product Product(int categoryId, int stock, decimal price) => new()
  {
    Id = 1,
    Name = "Producto",
    Description = "descripcion",
    SKU = "SKU-1",
    ImageUrl = "/img/foto.png",
    Price = price,
    Stock = stock,
    CategoryId = categoryId
  };
}
