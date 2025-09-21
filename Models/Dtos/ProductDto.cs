namespace ApiEcommerce.Models.Dtos;


public class ProductDto
{

  public int Id { get; set; }

  public required string Name { get; set; }

  public string? Description { get; set; }

  public decimal Price { get; set; }

  public string? ImageUrl { get; set; }

  public required string SKU { get; set; }

  public int Stock { get; set; }

  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; } = null;


  // Foreign Key --------
  public int CategoryId { get; set; }


}
