using System.ComponentModel.DataAnnotations;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Features.Ordering.Models;


/// <summary>Estado de una orden. Es la línea de tiempo que ve el comprador.</summary>
public enum OrderStatus
{
  Placed = 0,
  Paid = 1,
  Preparing = 2,
  Shipped = 3,
  Delivered = 4,
  Cancelled = 5
}


/// <summary>Estado del comprobante en PDF, que se genera aparte de la compra.</summary>
/// <remarks>
/// La generación es asíncrona; sin este campo, "todavía no hay comprobante" y "el
/// comprobante falló" serían indistinguibles para el cliente.
/// </remarks>
public enum ReceiptStatus
{
  Pending = 0,
  Available = 1,
  Failed = 2
}


/// <summary>Una compra registrada: qué se compró, a cuánto y a quién se envía.</summary>
/// <remarks>
/// Los datos del producto y del cliente se copian, no se referencian: el comprobante debe
/// seguir diciendo lo que se cobró aunque el catálogo cambie después.
/// </remarks>
public class Order : IAuditable
{
  public int Id { get; set; }

  /// <summary>Número legible (<c>ORD-2026-000012</c>). Es lo que cita el cliente.</summary>
  /// <remarks>
  /// Columna real con índice único, no una propiedad calculada: se busca por él.
  /// </remarks>
  [Required]
  [MaxLength(32)]
  public required string Number { get; set; }

  [Required]
  [MaxLength(450)]
  public required string BuyerUserId { get; set; }

  public OrderStatus Status { get; set; } = OrderStatus.Paid;

  /// <summary>Moneda ISO-4217 (<c>USD</c>).</summary>
  [Required]
  [MaxLength(3)]
  public string Currency { get; set; } = "USD";

  // ---- totales, desglosados como en el comprobante ------------------------
  // Se guardan calculados: el desglose impreso debe reproducirse aunque cambien impuestos o envío.

  public decimal Subtotal { get; set; }
  public decimal Discount { get; set; }
  public decimal Tax { get; set; }
  public decimal Shipping { get; set; }
  public decimal Total { get; set; }

  // ---- copia de los datos del cliente -------------------------------------
  // Congelados: el comprobante emitido hoy sigue mostrando el email al que se envió.

  [Required]
  [MaxLength(200)]
  public required string CustomerName { get; set; }

  [MaxLength(256)]
  public string? CustomerEmail { get; set; }

  [MaxLength(64)]
  public string? CustomerPhone { get; set; }

  [MaxLength(500)]
  public string? ShippingAddress { get; set; }

  public DateTime PlacedAt { get; set; } = DateTime.Now;

  // ---- comprobante ---------------------------------------------------------

  public ReceiptStatus ReceiptStatus { get; set; } = ReceiptStatus.Pending;

  /// <summary>Clave opaca del documento en el almacén. <c>null</c> mientras no exista.</summary>
  /// <remarks>
  /// No es una ruta de disco: solo la entiende <c>IDocumentStore</c>, así que cambiar de
  /// almacén no obliga a reescribir las filas.
  /// </remarks>
  [MaxLength(256)]
  public string? ReceiptDocumentKey { get; set; }

  public DateTime? ReceiptGeneratedAt { get; set; }

  public ICollection<OrderItem> Items { get; set; } = [];

  public DateTime CreatedAt { get; set; }

  public DateTime? UpdatedAt { get; set; }
}


/// <summary>Una línea de la orden, con el precio congelado.</summary>
public class OrderItem : IEntity
{
  public int Id { get; set; }

  public int OrderId { get; set; }

  public Order? Order { get; set; }

  /// <summary>Referencia al producto, solo informativa.</summary>
  /// <remarks>
  /// Sin clave foránea a propósito: la orden y su comprobante sobreviven al borrado del
  /// producto.
  /// </remarks>
  public int ProductId { get; set; }

  [Required]
  [MaxLength(50)]
  public required string Sku { get; set; }

  [Required]
  [MaxLength(200)]
  public required string Name { get; set; }

  public decimal UnitPrice { get; set; }

  public int Quantity { get; set; }

  /// <summary><c>UnitPrice × Quantity</c>, guardado.</summary>
  /// <remarks>
  /// Redundante a propósito: es lo que se imprimió, y recalcularlo al leer lo haría
  /// depender del redondeo de hoy.
  /// </remarks>
  public decimal LineTotal { get; set; }
}
