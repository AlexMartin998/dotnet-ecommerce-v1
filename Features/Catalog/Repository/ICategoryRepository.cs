
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

  /// <summary>Las destacadas, por su posición.</summary>
  Task<IReadOnlyList<Category>> GetFeaturedAsync(CancellationToken ct = default);

  /// <summary>Cuántos de esos ids existen.</summary>
  Task<int> CountExistingAsync(IReadOnlyCollection<int> ids, CancellationToken ct = default);

  /// <summary>
  /// Deja destacadas exactamente <paramref name="orderedIds"/>, en ese orden. Tiene que ir
  /// dentro de una transacción.
  /// </summary>
  /// <remarks>
  /// Primero las quita todas y luego pone las nuevas: al revés, reordenar [1,2] a [2,1]
  /// chocaría con el índice único a mitad. Toma un <c>sp_getapplock</c> para que dos
  /// administradores no intercalen sus sentencias.
  /// </remarks>
  Task ReplaceFeaturedAsync(IReadOnlyList<int> orderedIds, CancellationToken ct = default);

}
