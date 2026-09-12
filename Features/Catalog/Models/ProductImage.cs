using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Catalog.Models;


/// <summary>Una imagen pública de un producto.</summary>
/// <remarks>
/// Tabla aparte y no una colección primitiva como <c>Tags</c>: cada imagen tiene atributos
/// propios (su orden, y mañana su texto alternativo) y se borra una a una.
/// </remarks>
public class ProductImage
{
  [Key]
  public int Id { get; set; }

  public int ProductId { get; set; }

  public Product? Product { get; set; }

  /// <summary>Ruta pública servida desde <c>wwwroot/</c>.</summary>
  /// <remarks>
  /// Es una imagen de catálogo: pública a propósito, al revés que un comprobante. Lo que se
  /// guarda es la ruta relativa, no una absoluta con el host.
  /// </remarks>
  [Required]
  [MaxLength(400)]
  public required string Url { get; set; }

  /// <summary>Orden de presentación. La 0 es la que se enseña en el listado.</summary>
  public int Position { get; set; }
}
