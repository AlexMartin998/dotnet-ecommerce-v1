using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>
/// Body del PATCH de producto. Todos los campos son nullable a propósito: un
/// <c>decimal Price</c> no-nullable llegaría como 0 cuando el cliente no lo envía
/// y borraría el precio. El profile solo mapea los miembros no nulos.
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

  [MaxLength(300, ErrorMessage = "ImageUrl can't be longer than 300 characters")]
  [Url(ErrorMessage = "ImageUrl must be a valid absolute URL")]
  public string? ImageUrl { get; set; }

  [MaxLength(50, ErrorMessage = "SKU can't be longer than 50 characters")]
  [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "SKU can only contain letters, digits and hyphens")]
  public string? SKU { get; set; }

  [Range(0, int.MaxValue, ErrorMessage = "Stock must be zero or greater")]
  public int? Stock { get; set; }

  [Range(1, int.MaxValue, ErrorMessage = "CategoryId must be greater than zero")]
  public int? CategoryId { get; set; }


  /// <summary>
  /// Versión que el cliente leyó. <b>No la manda en el cuerpo</b>: la rellena el controller
  /// desde la cabecera <c>If-Match</c>.
  /// </summary>
  /// <remarks>
  /// Está aquí y no como parámetro de <c>ICrudService.UpdateAsync</c> para no meter una
  /// preocupación de HTTP en el contrato genérico del CRUD, que lo comparten todas las
  /// entidades. El DTO ya es el transporte entre controller y servicio; esto es un campo
  /// más de ese transporte.
  /// </remarks>
  public string? RowVersion { get; set; }

}
