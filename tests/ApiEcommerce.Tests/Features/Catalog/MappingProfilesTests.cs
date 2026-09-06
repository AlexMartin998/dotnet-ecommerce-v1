using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Mapping;
using ApiEcommerce.Features.Catalog.Models;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiEcommerce.Tests.Features.Catalog;


/// <summary>
/// Perfiles de AutoMapper. El grueso de los tests está en el PATCH parcial, que es lo
/// más fácil de romper sin que nada avise.
/// </summary>
public class MappingProfilesTests
{
  private readonly IMapper _mapper = new MapperConfiguration(
      cfg => cfg.AddMaps(typeof(CategoryProfile).Assembly),
      NullLoggerFactory.Instance).CreateMapper();

  [Fact]
  public void Configuration_IsValid()
  {
    // Detecta miembros del destino que ningún miembro del origen alimenta: avisa cuando
    // se añade una propiedad y se olvida el perfil.
    _mapper.ConfigurationProvider.AssertConfigurationIsValid();
  }

  // ---- PATCH parcial: lo que no viene, no se toca -------------------------

  [Fact]
  public void UpdateProduct_WithOnlyOneField_LeavesEverythingElseIntact()
  {
    // `Condition` recibe el valor ya convertido al tipo del destino, así que un `int?`
    // nulo llega como 0 y machaca el campo: con CategoryId = 0 se rompe la FK y sale un
    // 500 sin relación aparente. Por eso el PATCH se expresa campo a campo.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _mapper.Map(new UpdateProductDto { Name = "Nombre nuevo" }, existing);

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

    _mapper.Map(new UpdateProductDto(), existing);

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

    _mapper.Map(new UpdateProductDto { Stock = 0 }, existing);

    Assert.Equal(0, existing.Stock);
  }

  [Fact]
  public void UpdateProduct_ClearsAnOptionalFieldWithAnEmptyString()
  {
    // Semántica del PATCH: null = "no tocar", "" = "vaciar". Sin distinguirlos no habría
    // forma de borrar una descripción.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _mapper.Map(new UpdateProductDto { Description = "" }, existing);

    Assert.Equal("", existing.Description);
  }

  [Fact]
  public void UpdateCategory_WithOnlyTheDescription_KeepsTheName()
  {
    var existing = new Category { Id = 1, Name = "Bebidas", Description = "vieja" };

    _mapper.Map(new UpdateCategoryDto { Description = "nueva" }, existing);

    Assert.Equal("Bebidas", existing.Name);
    Assert.Equal("nueva", existing.Description);
  }

  // ---- auditoría: la estampa la base, no el mapper ------------------------

  [Fact]
  public void WritingNeverTouchesTheAuditFields()
  {
    // Los estampa AppDbContext: si el perfil los mapeara, un PATCH pisaría CreatedAt con
    // el default de un DTO que ni siquiera tiene esa propiedad.
    var created = new DateTime(2020, 1, 1);
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);
    existing.CreatedAt = created;
    existing.UpdatedAt = null;

    _mapper.Map(new UpdateProductDto { Name = "otro" }, existing);

    Assert.Equal(created, existing.CreatedAt);
    Assert.Null(existing.UpdatedAt);
  }

  [Fact]
  public void CreateProduct_DoesNotCarryAnIdOrTheNavigation()
  {
    var entity = _mapper.Map<Product>(new CreateProductDto
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

    Assert.Equal("Bebidas", _mapper.Map<ProductDto>(entity).CategoryName);
  }

  [Fact]
  public void ProductToDto_WithoutTheIncludedNavigation_LeavesTheNameNullAndDoesNotThrow()
  {
    // Es lo que pasa si una consulta olvida el .Include: el mapeo no puede reventar, sale
    // null y se ve en la respuesta.
    Assert.Null(_mapper.Map<ProductDto>(Product(categoryId: 3, stock: 10, price: 99.9m)).CategoryName);
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
