using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Persistence;
using AutoMapper;
using Moq;

namespace ApiEcommerce.Tests.Shared.Crud;


/// <summary>
/// Invariantes del CRUD compartido. Son las que <c>CrudService</c> es <c>sealed</c>
/// para proteger, así que son exactamente las que hay que fijar con tests.
/// </summary>
public class CrudServiceTests
{
  private readonly Mock<IBaseRepository<Category>> _repository = new();
  private readonly Mock<IMapper> _mapper = new();
  private readonly Mock<IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>> _rules = new();

  public CrudServiceTests() => _rules.SetupGet(r => r.EntityName).Returns("Category");

  private CrudService<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto> Sut()
      => new(_repository.Object, _mapper.Object, _rules.Object);

  // ---- 404 ----------------------------------------------------------------

  [Fact]
  public async Task GetByIdAsync_WhenTheRepositoryReturnsNull_ThrowsNotFound()
  {
    // El repositorio devuelve null (no lanza); quien decide que eso es un 404 es el
    // servicio. Ese reparto es la regla de capas, y este test es su red.
    _repository.Setup(r => r.GetByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync((Category?)null);

    var ex = await Assert.ThrowsAsync<NotFoundAppException>(() => Sut().GetByIdAsync(9));

    // El mensaje usa EntityName, no typeof(TEntity).Name: renombrar la clase C# no
    // debe cambiar el contrato de la API.
    Assert.Equal("Category with key '9' was not found.", ex.Message);
  }

  [Theory]
  [InlineData("update")]
  [InlineData("delete")]
  public async Task WritingOverAMissingId_ThrowsNotFoundBeforeTouchingTheRules(string operation)
  {
    _repository.Setup(r => r.GetByIdAsync(9, It.IsAny<CancellationToken>())).ReturnsAsync((Category?)null);

    await Assert.ThrowsAsync<NotFoundAppException>(() => operation == "update"
        ? Sut().UpdateAsync(9, new UpdateCategoryDto())
        : Sut().DeleteAsync(9));

    // Una regla de negocio sobre una entidad que no existe no significa nada.
    _rules.Verify(r => r.EnsureCanUpdateAsync(
        It.IsAny<int>(), It.IsAny<UpdateCategoryDto>(), It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
    _rules.Verify(r => r.EnsureCanDeleteAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  // ---- las reglas corren ANTES de escribir --------------------------------

  [Fact]
  public async Task CreateAsync_EvaluatesTheRulesBeforeWriting()
  {
    var order = new List<string>();
    var entity = new Category { Id = 42, Name = "Bebidas" };

    _rules.Setup(r => r.EnsureCanCreateAsync(It.IsAny<CreateCategoryDto>(), It.IsAny<CancellationToken>()))
          .Callback(() => order.Add("rules")).Returns(Task.CompletedTask);
    _mapper.Setup(m => m.Map<Category>(It.IsAny<CreateCategoryDto>())).Returns(entity);
    _repository.Setup(r => r.AddAsync(entity, It.IsAny<CancellationToken>()))
               .Callback(() => order.Add("repository")).ReturnsAsync(entity);

    var id = await Sut().CreateAsync(new CreateCategoryDto { Name = "Bebidas" });

    Assert.Equal(["rules", "repository"], order);
    // El id sale de IEntity, sin reflexión: es lo que el controller necesita para el
    // CreatedAtRoute.
    Assert.Equal(42, id);
  }

  [Fact]
  public async Task CreateAsync_WhenARuleThrows_NothingIsWritten()
  {
    _rules.Setup(r => r.EnsureCanCreateAsync(It.IsAny<CreateCategoryDto>(), It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConflictAppException("ya existe"));

    await Assert.ThrowsAsync<ConflictAppException>(
        () => Sut().CreateAsync(new CreateCategoryDto { Name = "Bebidas" }));

    _repository.Verify(r => r.AddAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  [Fact]
  public async Task UpdateAsync_EvaluatesTheRulesBeforeMapping()
  {
    // ⚠️ El orden es la razón de ser de la firma: la regla recibe `existing` y debe
    // verlo con el estado PREVIO. Si el mapeo corriera antes, una regla del tipo
    // "no se puede bajar el precio más de un 50%" compararía el valor nuevo consigo mismo.
    var order = new List<string>();
    var existing = new Category { Id = 7, Name = "Bebidas" };

    _repository.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    _rules.Setup(r => r.EnsureCanUpdateAsync(7, It.IsAny<UpdateCategoryDto>(), existing, It.IsAny<CancellationToken>()))
          .Callback(() => order.Add("rules")).Returns(Task.CompletedTask);
    _mapper.Setup(m => m.Map(It.IsAny<UpdateCategoryDto>(), existing))
           .Callback(() => order.Add("map")).Returns(existing);
    _repository.Setup(r => r.UpdateAsync(existing, It.IsAny<CancellationToken>()))
               .Callback(() => order.Add("repository")).ReturnsAsync(existing);

    await Sut().UpdateAsync(7, new UpdateCategoryDto { Name = "Snacks" });

    Assert.Equal(["rules", "map", "repository"], order);
  }

  [Fact]
  public async Task UpdateAsync_MapsOntoTheTrackedInstance()
  {
    // No se construye una entidad nueva: se mapea SOBRE la que devolvió el repositorio,
    // que es la que rastrea el change tracker. Mapear a una instancia nueva dejaría en
    // 0/null todo lo que el PATCH no trae.
    var existing = new Category { Id = 7, Name = "Bebidas" };
    _repository.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    _repository.Setup(r => r.UpdateAsync(It.IsAny<Category>(), It.IsAny<CancellationToken>())).ReturnsAsync(existing);

    await Sut().UpdateAsync(7, new UpdateCategoryDto { Name = "Snacks" });

    _mapper.Verify(m => m.Map(It.IsAny<UpdateCategoryDto>(), existing), Times.Once);
    _mapper.Verify(m => m.Map<Category>(It.IsAny<UpdateCategoryDto>()), Times.Never);
  }

  [Fact]
  public async Task DeleteAsync_EvaluatesTheRulesBeforeDeleting()
  {
    var existing = new Category { Id = 7, Name = "Bebidas" };
    _repository.Setup(r => r.GetByIdAsync(7, It.IsAny<CancellationToken>())).ReturnsAsync(existing);
    _rules.Setup(r => r.EnsureCanDeleteAsync(existing, It.IsAny<CancellationToken>()))
          .ThrowsAsync(new ConflictAppException("tiene productos"));

    await Assert.ThrowsAsync<ConflictAppException>(() => Sut().DeleteAsync(7));

    _repository.Verify(r => r.DeleteAsync(It.IsAny<int>(), It.IsAny<CancellationToken>()), Times.Never);
  }

  // ---- listados -----------------------------------------------------------

  [Fact]
  public async Task GetAllAsync_WithNoRows_ReturnsAnEmptyCollectionAndNotAnError()
  {
    _repository.Setup(r => r.GetAllAsync(It.IsAny<CancellationToken>())).ReturnsAsync([]);
    _mapper.Setup(m => m.Map<IEnumerable<CategoryDto>>(It.IsAny<IEnumerable<Category>>())).Returns([]);

    Assert.Empty(await Sut().GetAllAsync());
  }

  [Fact]
  public async Task GetPagedAsync_KeepsTheMetadataOfTheRepositoryPage()
  {
    // El servicio mapea los items pero NO recalcula los metadatos: el total viene del
    // COUNT que hizo la base.
    _repository.Setup(r => r.GetPagedAsync(2, 10, It.IsAny<CancellationToken>()))
               .ReturnsAsync(new PagedResult<Category>([], 2, 10, 137));
    _mapper.Setup(m => m.Map<IEnumerable<CategoryDto>>(It.IsAny<IEnumerable<Category>>())).Returns([]);

    var page = await Sut().GetPagedAsync(new PageQuery { Page = 2, PageSize = 10 });

    Assert.Equal(2, page.Page);
    Assert.Equal(137, page.TotalItems);
    Assert.Equal(14, page.TotalPages);
  }

  [Fact]
  public async Task NullArguments_FailFast()
  {
    await Assert.ThrowsAsync<ArgumentNullException>(() => Sut().GetPagedAsync(null!));
    await Assert.ThrowsAsync<ArgumentNullException>(() => Sut().CreateAsync(null!));
    await Assert.ThrowsAsync<ArgumentNullException>(() => Sut().UpdateAsync(1, null!));
  }
}
