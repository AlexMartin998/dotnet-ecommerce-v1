using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>Body del POST de producto.</summary>
public class CreateProductDto
{

  [Required(ErrorMessage = "Name is required")]
  [MaxLength(100, ErrorMessage = "Name can't be longer than 100 characters")]
  [MinLength(3, ErrorMessage = "Name can't be shorter than 3 characters")]
  public string Name { get; set; } = string.Empty;

  [MaxLength(500, ErrorMessage = "Description can't be longer than 500 characters")]
  public string? Description { get; set; }

  [Range(0, 9999999999999999.99, ErrorMessage = "Price must be zero or greater")]
  public decimal Price { get; set; }

  /// <summary>Opcional: si no viene, se deriva del nombre.</summary>
  [MaxLength(200, ErrorMessage = "Slug can't be longer than 200 characters")]
  [RegularExpression(@"^[a-z0-9]+(?:-[a-z0-9]+)*$",
      ErrorMessage = "Slug can only contain lowercase letters, digits and hyphens")]
  public string? Slug { get; set; }

  /// <summary>Etiquetas de navegación y búsqueda.</summary>
  [MaxLength(20, ErrorMessage = "A product can't have more than 20 tags")]
  public List<string> Tags { get; set; } = [];

  /// <summary>Tallas o presentaciones. No afectan al stock, que es por producto.</summary>
  [MaxLength(20, ErrorMessage = "A product can't have more than 20 sizes")]
  public List<string> Sizes { get; set; } = [];

  [Required(ErrorMessage = "SKU is required")]
  [MaxLength(50, ErrorMessage = "SKU can't be longer than 50 characters")]
  [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "SKU can only contain letters, digits and hyphens")]
  public string SKU { get; set; } = string.Empty;

  [Range(0, int.MaxValue, ErrorMessage = "Stock must be zero or greater")]
  public int Stock { get; set; }

  [Range(1, int.MaxValue, ErrorMessage = "CategoryId is required and must be greater than zero")]
  public int CategoryId { get; set; }

}
