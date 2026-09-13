namespace ApiEcommerce.Features.Catalog.Dtos;


/// <summary>Producto tal y como lo ve el cliente, con el nombre de su categoría.</summary>
public class ProductDto
{

  public int Id { get; set; }

  public string Name { get; set; } = string.Empty;

  /// <summary>Identificador de la URL pública: <c>/product/{slug}</c>.</summary>
  public string Slug { get; set; } = string.Empty;

  public string? Description { get; set; }

  public decimal Price { get; set; }

  /// <summary>Imágenes públicas, en orden. La primera es la del listado.</summary>
  public IReadOnlyList<string> Images { get; set; } = [];

  public string SKU { get; set; } = string.Empty;

  /// <summary>Etiquetas de navegación y búsqueda.</summary>
  public IReadOnlyList<string> Tags { get; set; } = [];

  /// <summary>Etiquetas de las tallas activas, en orden. Derivado de <see cref="Variants"/>.</summary>
  /// <remarks>Se mantiene por compatibilidad; para vender, usar <see cref="Variants"/>.</remarks>
  public IReadOnlyList<string> Sizes { get; set; } = [];

  /// <summary>Suma del stock de las variantes activas.</summary>
  public int Stock { get; set; }

  /// <summary>Lo que se vende: las variantes activas, en orden. Siempre al menos una si hay algo a la venta.</summary>
  /// <remarks>
  /// La línea de carrito se cita por <c>variants[].sku</c>, también en un producto sin tallas:
  /// su única variante tiene <c>size</c> null y nace con el SKU del producto.
  /// </remarks>
  public IReadOnlyList<ProductVariantDto> Variants { get; set; } = [];

  public DateTime CreatedAt { get; set; }
  public DateTime? UpdatedAt { get; set; }


  // Se expone el id, no la navegación: el nombre viaja plano porque lo necesita el listado.
  public int CategoryId { get; set; }
  public string? CategoryName { get; set; }
  public string? CategorySlug { get; set; }


  /// <summary>
  /// Versión del recurso, en base64. El controller la publica como cabecera <c>ETag</c>.
  /// </summary>
  /// <remarks>
  /// Es opaca para el cliente: solo tiene que devolverla tal cual en <c>If-Match</c>.
  /// </remarks>
  public string? RowVersion { get; set; }

}


/// <summary>Contadores del catálogo, para el panel de administración.</summary>
public class ProductStatsDto
{
  public int Total { get; set; }

  /// <summary>Stock 0: no se puede vender.</summary>
  public int OutOfStock { get; set; }

  /// <summary>Stock entre 1 y el umbral. NO incluye el 0.</summary>
  /// <remarks>
  /// Mezclar el agotado con el escaso hace que el panel pida reponer lo que ya no se puede
  /// vender, y esconde cuánto se está perdiendo por rotura de stock.
  /// </remarks>
  public int LowStock { get; set; }

  /// <summary>Hasta dónde se considera «bajo», para que el panel lo pueda decir.</summary>
  public int LowStockThreshold { get; set; }
}
