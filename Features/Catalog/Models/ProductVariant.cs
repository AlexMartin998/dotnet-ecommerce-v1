using System.ComponentModel.DataAnnotations;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Features.Catalog.Models;


/// <summary>Lo que de verdad se vende de un producto: una talla, con su SKU y su stock.</summary>
/// <remarks>
/// <para>
/// Todo producto tiene al menos una. Uno que se vende sin tallas tiene <b>una sola, sin
/// talla</b>, con el SKU del producto: así hay un único camino para apartar y devolver stock,
/// en vez de uno para productos con talla y otro para los demás (<c>planning/27</c>).
/// </para>
/// <para>
/// No se borra, se desactiva: las líneas de orden la referencian con clave foránea, y un
/// comprobante de ayer tiene que seguir diciendo qué talla se vendió.
/// </para>
/// </remarks>
public class ProductVariant : IAuditable
{
  [Key]
  public int Id { get; set; }

  public int ProductId { get; set; }

  public Product? Product { get; set; }

  /// <summary>Etiqueta de la talla (<c>M</c>, <c>XL</c>). <c>null</c> en la variante sin talla.</summary>
  /// <remarks>No cambia tras crearla: para otra talla, otra variante.</remarks>
  [MaxLength(20)]
  public string? Size { get; set; }

  /// <summary>La clave de la línea de carrito. Única en toda la tabla.</summary>
  /// <remarks>
  /// No cambia tras crearla: vive en los carritos que el front guarda en <c>localStorage</c>
  /// y en las líneas de las órdenes.
  /// </remarks>
  [Required]
  [MaxLength(50)]
  public required string SKU { get; set; }

  /// <summary>Unidades disponibles. Se descuenta con un UPDATE condicional, nunca leyendo antes.</summary>
  [Range(0, int.MaxValue)]
  public int Stock { get; set; }

  /// <summary>Orden de presentación en la ficha.</summary>
  public int Position { get; set; }

  /// <summary>Si se puede vender. Desactivar es la forma de retirar una talla.</summary>
  public bool IsActive { get; set; } = true;

  /// <summary>Cuándo se retiró su producto. Copia de <c>Product.DeletedAt</c>.</summary>
  /// <remarks>
  /// Redundante a propósito: el índice único de <see cref="SKU"/> tiene que ir filtrado por
  /// el borrado lógico, o un producto retirado bloquearía para siempre el SKU de su variante
  /// sin talla —que es el del producto—, y el filtro de un índice no puede mirar otra tabla.
  /// Lo estampa <c>ProductRepository.SoftDeleteAsync</c> en la misma transacción.
  /// </remarks>
  public DateTime? DeletedAt { get; set; }

  // Estampados por AppDbContext.SaveChangesAsync (ver IAuditable). No asignar a mano.
  public DateTime CreatedAt { get; set; } = DateTime.Now;
  public DateTime? UpdatedAt { get; set; }

  /// <summary>Token de concurrencia optimista para el PATCH de administración.</summary>
  /// <remarks>
  /// SQL Server lo cambia también con el UPDATE de una compra, y eso es justo lo que se
  /// quiere: un administrador que repone con lo que leyó antes de una venta recibe 412 en vez
  /// de pisar el descuento.
  /// </remarks>
  [Timestamp]
  public byte[]? RowVersion { get; set; }
}
