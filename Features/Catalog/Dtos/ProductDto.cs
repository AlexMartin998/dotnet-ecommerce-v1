namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>Producto tal y como lo ve el cliente, con el nombre de su categoría.</summary>
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


  // Se expone el id, no la navegación: el nombre viaja plano porque lo necesita el listado.
  public int CategoryId { get; set; }
  public string? CategoryName { get; set; }


  /// <summary>
  /// Versión del recurso, en base64. El controller la publica como cabecera <c>ETag</c>.
  /// </summary>
  /// <remarks>
  /// Es opaca para el cliente: solo tiene que devolverla tal cual en <c>If-Match</c>.
  /// </remarks>
  public string? RowVersion { get; set; }

}
