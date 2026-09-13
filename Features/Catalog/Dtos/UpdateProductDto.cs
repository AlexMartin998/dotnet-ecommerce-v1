using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>
/// Body del PATCH de producto. Todos los campos son nullable a propósito: un
/// <c>decimal</c> no-nullable llegaría como 0 y borraría el valor que ya había.
/// </summary>
public class UpdateProductDto
{

  [MaxLength(100, ErrorMessage = "Name can't be longer than 100 characters")]
  [MinLength(3, ErrorMessage = "Name can't be shorter than 3 characters")]
  public string? Name { get; set; }

  [MaxLength(500, ErrorMessage = "Description can't be longer than 500 characters")]
  public string? Description { get; set; }

  [Range(0, 9999999999999999.99, ErrorMessage = "Price must be zero or greater")]
  public decimal? Price { get; set; }

  /// <summary>Las etiquetas se REEMPLAZAN enteras; omitirlas las deja como estaban.</summary>
  [MaxLength(20, ErrorMessage = "A product can't have more than 20 tags")]
  public List<string>? Tags { get; set; }

  // // Fuera en planning/27: las tallas son variantes y se editan en /product/{id}/variants.
  // [MaxLength(20, ErrorMessage = "A product can't have more than 20 sizes")]
  // public List<string>? Sizes { get; set; }

  [MaxLength(50, ErrorMessage = "SKU can't be longer than 50 characters")]
  [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "SKU can only contain letters, digits and hyphens")]
  public string? SKU { get; set; }

  // // Fuera en planning/27: el stock es de la variante, también en un producto sin tallas.
  // [Range(0, int.MaxValue, ErrorMessage = "Stock must be zero or greater")]
  // public int? Stock { get; set; }

  [Range(1, int.MaxValue, ErrorMessage = "CategoryId must be greater than zero")]
  public int? CategoryId { get; set; }


  /// <summary>
  /// Versiones que el cliente dice haber leído, tomadas de la cabecera <c>If-Match</c>.
  /// </summary>
  /// <remarks>
  /// Es una lista porque el RFC 9110 permite <c>If-Match: "a", "b"</c> y basta con que una
  /// case. <c>[JsonIgnore]</c> impide enlazarla desde el cuerpo: la rellena el controller.
  /// Vive en el DTO para no meter una preocupación de HTTP en <c>ICrudService</c>.
  /// </remarks>
  [JsonIgnore]
  public IReadOnlyList<string>? IfMatch { get; set; }

}
