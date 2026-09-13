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
    Assert.Equal(10, Assert.Single(existing.Variants).Stock);
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
    Assert.Equal(10, Assert.Single(existing.Variants).Stock);
    Assert.Equal(99.9m, existing.Price);
    Assert.Equal(["ropa"], existing.Tags);
  }

  [Fact]
  public void UpdateProduct_CanSetAZeroOnPurpose()
  {
    // "No viene" es null y no 0: un precio 0 legítimo (un regalo) tiene que aplicarse. Antes
    // se probaba con el stock, que desde planning/27 se edita en la variante.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto { Price = 0m }, existing);

    Assert.Equal(0m, existing.Price);
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

  // ---- slug, etiquetas e imágenes ------------------------------------------

  [Fact]
  public void CreateProduct_DerivesTheSlugFromTheNameWhenItIsNotSent()
  {
    var entity = _products.ToEntity(new CreateProductDto
    {
      Name = "  Camión Ñandú 4x4  ", SKU = "SKU-9", Price = 1m, Stock = 1, CategoryId = 3
    });

    // Sin acentos: "Camión" y "Camion" tienen que dar el mismo slug o el índice único no
    // los ve como el mismo producto.
    Assert.Equal("camion-nandu-4x4", entity.Slug);
  }

  [Fact]
  public void CreateProduct_KeepsTheSlugTheClientSends()
  {
    var entity = _products.ToEntity(new CreateProductDto
    {
      Name = "Otro nombre", Slug = "el-mio", SKU = "SKU-9", Price = 1m, Stock = 1, CategoryId = 3
    });

    Assert.Equal("el-mio", entity.Slug);
  }

  [Fact]
  public void UpdateProduct_NeverTouchesTheSlug()
  {
    // Cambiarlo rompería los enlaces que ya circulan.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto { Name = "Nombre nuevo" }, existing);

    Assert.Equal("producto", existing.Slug);
  }

  [Fact]
  public void UpdateProduct_ReplacesTheTagsInsteadOfMergingThem()
  {
    // Fusionar no dejaría forma de QUITAR una etiqueta.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto { Tags = ["oferta"] }, existing);

    Assert.Equal(["oferta"], existing.Tags);
  }

  [Fact]
  public void UpdateProduct_WithoutTags_LeavesThemAlone()
  {
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _products.Apply(new UpdateProductDto { Name = "x" }, existing);

    Assert.Equal(["ropa"], existing.Tags);
  }

  [Fact]
  public void ProductToDto_ReturnsTheImagesInOrder()
  {
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);
    existing.Images =
    [
      new ProductImage { Url = "/b.png", Position = 1 },
      new ProductImage { Url = "/a.png", Position = 0 }
    ];

    // El orden es parte del dato: la 0 es la que se enseña en el listado.
    Assert.Equal(["/a.png", "/b.png"], _products.ToDto(existing).Images);
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

  [Fact]
  public void ProductToDto_OrdersImagesByPositionThenById()
  {
    // El front enseña la segunda al pasar el ratón: con la misma posición no pueden bailar.
    var product = Product(categoryId: 3, stock: 1, price: 1m);
    product.Images =
    [
      new ProductImage { Id = 9, Url = "/b.jpg", Position = 1 },
      new ProductImage { Id = 7, Url = "/c.jpg", Position = 1 },
      new ProductImage { Id = 8, Url = "/a.jpg", Position = 0 }
    ];

    Assert.Equal(["/a.jpg", "/c.jpg", "/b.jpg"], _products.ToDto(product).Images);
  }

  private static Product Product(int categoryId, int stock, decimal price) => new()
  {
    Id = 1,
    Name = "Producto",
    Description = "descripcion",
    SKU = "SKU-1",
    Slug = "producto",
    Tags = ["ropa"],
    Price = price,
    CategoryId = categoryId,
    Variants = [new ProductVariant { Id = 1, SKU = "SKU-1", Stock = stock }]
  };
}


/// <summary>El SKU por defecto de una talla, que la regla y el mapeador derivan igual.</summary>
public class VariantSkusTests
{
  [Theory]
  [InlineData("TSH-01", "M", "TSH-01-M")]
  [InlineData("TSH-01", "xl", "TSH-01-XL")]
  [InlineData("TSH-01", "One size", "TSH-01-ONE-SIZE")]
  [InlineData("TSH-01", " 1/2 ", "TSH-01-1-2")]
  public void For_JoinsTheProductSkuWithTheNormalizedSize(string productSku, string size, string expected)
      => Assert.Equal(expected, ApiEcommerce.Features.Catalog.Service.VariantSkus.For(productSku, size));
}
