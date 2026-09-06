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

  [MaxLength(300, ErrorMessage = "ImageUrl can't be longer than 300 characters")]
  [Url(ErrorMessage = "ImageUrl must be a valid absolute URL")]
  public string? ImageUrl { get; set; }

  [Required(ErrorMessage = "SKU is required")]
  [MaxLength(50, ErrorMessage = "SKU can't be longer than 50 characters")]
  [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "SKU can only contain letters, digits and hyphens")]
  public string SKU { get; set; } = string.Empty;

  [Range(0, int.MaxValue, ErrorMessage = "Stock must be zero or greater")]
  public int Stock { get; set; }

  [Range(1, int.MaxValue, ErrorMessage = "CategoryId is required and must be greater than zero")]
  public int CategoryId { get; set; }

}
