namespace ApiEcommerce.Models.Dtos;


public class ProductDto
{

  public int Id { get; set; }

  public string Name { get; set; } = string.Empty;

  public string? Description { get; set; }

  public decimal Price { get; set; }

  public string? ImageUrl { get; set; }

  public string SKU { get; set; } = string.Empty;

  public int Stock { get; set; }

  public DateTime CreatedAt { get; set; }
  public DateTime? UpdatedAt { get; set; }


  // Foreign Key --------
  // Se expone el id, no la navegación completa. El nombre viaja plano porque el
  // listado de productos lo necesita (ver AGENTS/docs/01-capas-y-contratos.md).
  public int CategoryId { get; set; }
  public string? CategoryName { get; set; }

}
