using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>Body de <c>POST /api/product/buy</c>: descuenta stock por SKU.</summary>
public class BuyProductDto
{

  [Required(ErrorMessage = "SKU is required")]
  [MaxLength(50, ErrorMessage = "SKU can't be longer than 50 characters")]
  public string SKU { get; set; } = string.Empty;

  [Range(1, int.MaxValue, ErrorMessage = "Quantity must be greater than zero")]
  public int Quantity { get; set; }

}
