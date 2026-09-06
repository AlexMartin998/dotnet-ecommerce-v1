using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Mapping;
using ApiEcommerce.Features.Catalog.Models;
using AutoMapper;
using Microsoft.Extensions.Logging.Abstractions;

namespace ApiEcommerce.Tests.Features.Catalog;


/// <summary>
/// Perfiles de AutoMapper. El PATCH parcial es donde este proyecto ya se quemó una vez,
/// así que es donde más tests hay.
/// </summary>
public class MappingProfilesTests
{
  private readonly IMapper _mapper = new MapperConfiguration(
      cfg => cfg.AddMaps(typeof(CategoryProfile).Assembly),
      NullLoggerFactory.Instance).CreateMapper();

  [Fact]
  public void Configuration_IsValid()
  {
    // Detecta miembros del destino que ningún miembro del origen alimenta. Es el test
    // que avisa cuando alguien añade una propiedad a una entidad y se olvida del perfil.
    _mapper.ConfigurationProvider.AssertConfigurationIsValid();
  }

  // ---- PATCH parcial: lo que no viene, no se toca -------------------------

  [Fact]
  public void UpdateProduct_WithOnlyOneField_LeavesEverythingElseIntact()
  {
    // ⚠️ EL bug que costó un 500. Con `.ForAllMembers(o => o.Condition(...))`, la
    // Condition recibe el valor YA CONVERTIDO al tipo del destino: un `int?` nulo
    // llegaba como 0, no se saltaba, y machacaba el campo. Con CategoryId = 0 se
    // rompía la clave foránea y el cliente recibía un 500 sin relación aparente.
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
    // La otra cara: 0 SÍ tiene que poder escribirse. "No viene" es null, no 0 — un
    // agotamiento de stock legítimo no puede quedarse sin aplicar.
    var existing = Product(categoryId: 3, stock: 10, price: 99.9m);

    _mapper.Map(new UpdateProductDto { Stock = 0 }, existing);

    Assert.Equal(0, existing.Stock);
  }

  [Fact]
  public void UpdateProduct_ClearsAnOptionalFieldWithAnEmptyString()
  {
    // Semántica documentada del PATCH: null = "no tocar", "" = "vaciar". Sin esta
    // distinción no habría forma de borrar una descripción.
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
    // AppDbContext.SaveChangesAsync los estampa. Si el perfil los mapeara, un PATCH
    // pisaría CreatedAt con el default de un DTO que ni siquiera tiene esa propiedad.
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
    // Es lo que pasa cuando una consulta olvida el .Include(p => p.Category). El mapeo
    // no puede reventar por eso: sale null y se ve en la respuesta.
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
