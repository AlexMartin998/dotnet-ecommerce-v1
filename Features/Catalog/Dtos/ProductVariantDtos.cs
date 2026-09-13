using System.ComponentModel.DataAnnotations;
using System.Text.Json.Serialization;

namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>Límites de las variantes, compartidos por los DTOs y el servicio.</summary>
public static class VariantLimits
{
  /// <summary>Tope de stock por talla.</summary>
  /// <remarks>
  /// Con <c>int.MaxValue</c> dos tallas desbordaban la suma del producto y tumbaban el
  /// catálogo público con un 500. Un millón por talla sobra para una tienda mediana.
  /// </remarks>
  public const int MaxStock = 1_000_000;

  /// <summary>Tope de tallas por producto, como el de imágenes.</summary>
  public const int MaxVariants = 30;
}


/// <summary>Una talla de un producto, con lo que el front necesita para venderla.</summary>
/// <remarks>
/// La ficha pública solo trae las activas; <c>GET /product/{id}/variants</c> (admin) trae
/// todas. Es el mismo DTO en los dos sitios: lo que cambia es el filtro, no la forma.
/// </remarks>
public class ProductVariantDto
{
  public int Id { get; set; }

  /// <summary>La clave de la línea de carrito: es lo que se manda en la cotización y en la orden.</summary>
  public string SKU { get; set; } = string.Empty;

  /// <summary>Etiqueta de la talla. <c>null</c> en un producto que se vende sin tallas.</summary>
  public string? Size { get; set; }

  public int Stock { get; set; }

  /// <summary>Activa y con stock. Lo que el front usa para deshabilitar la talla.</summary>
  public bool Available { get; set; }

  public int Position { get; set; }

  public bool IsActive { get; set; }

  /// <summary>Versión de la variante, para el <c>If-Match</c> de su PATCH.</summary>
  public string? RowVersion { get; set; }
}


/// <summary>Una talla nueva, al crear el producto o después.</summary>
public class CreateProductVariantDto
{
  [Required(ErrorMessage = "Size is required")]
  [MaxLength(20, ErrorMessage = "Size can't be longer than 20 characters")]
  [RegularExpression(@"^[A-Za-z0-9][A-Za-z0-9 .\/\-]*$",
      ErrorMessage = "Size can only contain letters, digits, spaces, dots, slashes and hyphens")]
  public string Size { get; set; } = string.Empty;

  /// <summary>Opcional: si no viene, es <c>{SKU del producto}-{talla}</c>.</summary>
  [MaxLength(50, ErrorMessage = "SKU can't be longer than 50 characters")]
  [RegularExpression(@"^[A-Za-z0-9\-]+$", ErrorMessage = "SKU can only contain letters, digits and hyphens")]
  public string? SKU { get; set; }

  [Range(0, VariantLimits.MaxStock, ErrorMessage = "Stock must be between 0 and 1000000")]
  public int Stock { get; set; }

  /// <summary>Opcional: si no viene, va al final.</summary>
  [Range(0, 1000, ErrorMessage = "Position must be between 0 and 1000")]
  public int? Position { get; set; }
}


/// <summary>Body del PATCH de una variante. Todo nullable: omitir es «no tocar».</summary>
/// <remarks>
/// Ni la talla ni el SKU se cambian: el SKU vive en carritos y órdenes. Para otra talla,
/// otra variante, y la vieja se desactiva.
/// </remarks>
public class UpdateProductVariantDto
{
  [Range(0, VariantLimits.MaxStock, ErrorMessage = "Stock must be between 0 and 1000000")]
  public int? Stock { get; set; }

  [Range(0, 1000, ErrorMessage = "Position must be between 0 and 1000")]
  public int? Position { get; set; }

  public bool? IsActive { get; set; }

  /// <summary>Versiones de la cabecera <c>If-Match</c>. La rellena el controller.</summary>
  [JsonIgnore]
  public IReadOnlyList<string>? IfMatch { get; set; }
}
