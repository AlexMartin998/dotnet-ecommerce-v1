
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Catalog.Models;
namespace ApiEcommerce.Features.Catalog.Repository;


/// <summary>CRUD de categorías más las consultas propias del dominio.</summary>
public interface ICategoryRepository : IBaseRepository<Category>
{

  /// <summary>¿Hay ya otra categoría con ese nombre? <paramref name="excludeId"/> se excluye de la comprobación.</summary>
  Task<bool> NameExistsAsync(string name, int? excludeId = null, CancellationToken ct = default);


  /// <summary>¿Hay ya una categoría con ese slug?</summary>
  Task<bool> SlugExistsAsync(string slug, CancellationToken ct = default);

  /// <summary>La categoría con ese slug, o <c>null</c>.</summary>
  Task<Category?> GetBySlugAsync(string slug, CancellationToken ct = default);

  /// <summary>¿La categoría tiene productos asociados?</summary>
  Task<bool> HasProductsAsync(int categoryId, CancellationToken ct = default);

}
