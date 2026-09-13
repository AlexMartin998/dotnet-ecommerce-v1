
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
namespace ApiEcommerce.Features.Catalog.Service;


/// <summary>
/// Servicio de categorías. El CRUD lo aporta <see cref="ICrudService{TDto, TCreateDto, TUpdateDto}"/>
/// y aquí solo se declara lo propio del dominio. La entidad no aparece: el controller no
/// puede verla.
/// </summary>
public interface ICategoryService
  : ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>
{
  /// <summary>La categoría con ese slug, que es lo que el front usa en la URL pública.</summary>
  /// <exception cref="Exceptions.NotFoundAppException">No hay ninguna con ese slug.</exception>
  Task<CategoryDto> GetBySlugAsync(string slug, CancellationToken ct = default);

  /// <summary>Las destacadas del header, por posición. Como mucho tres.</summary>
  Task<IReadOnlyList<CategoryDto>> GetFeaturedAsync(CancellationToken ct = default);

  /// <summary>Reemplaza las destacadas por esas, en ese orden, y devuelve cómo quedan.</summary>
  /// <exception cref="Exceptions.CustomAppException">400 <c>featured_limit_reached</c> con más de tres.</exception>
  /// <exception cref="Exceptions.BadOperationAppException">Ids repetidos o inexistentes.</exception>
  Task<IReadOnlyList<CategoryDto>> SetFeaturedAsync(IReadOnlyList<int> orderedIds, CancellationToken ct = default);
}
