using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>Categoría tal y como la ve el cliente.</summary>
public class CategoryDto
{

  public int Id { get; set; }
  public string Name { get; set; } = string.Empty;

  /// <summary>Identificador de la URL pública: <c>/category/slug/{slug}</c>.</summary>
  public string Slug { get; set; } = string.Empty;
  public string? Description { get; set; }

  /// <summary>Posición en el header (1..3), o <c>null</c> si no es destacada.</summary>
  public int? FeaturedPosition { get; set; }

}


/// <summary>Body de <c>PUT /category/featured</c>: las destacadas, en el orden del header.</summary>
/// <remarks>
/// Reemplaza la lista entera: marcar, desmarcar y reordenar son la misma operación. Vacía
/// quita todas. Sin <c>[MaxLength(3)]</c> a propósito: el límite es una regla de negocio con
/// su propio <c>code</c> (<c>featured_limit_reached</c>), no un 400 de validación genérico.
/// </remarks>
public class SetFeaturedCategoriesDto
{
  [Required]
  [MaxLength(50, ErrorMessage = "Too many category ids")]
  public List<int> CategoryIds { get; set; } = [];
}
