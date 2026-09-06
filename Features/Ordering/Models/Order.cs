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


/// <summary>Estado del comprobante en PDF, que se genera <b>aparte</b> de la compra.</summary>
/// <remarks>
/// Existe porque la generación es asíncrona: entre que la orden se crea y el PDF está
/// disponible pasa un rato, y el cliente necesita saber en cuál de los dos momentos está.
/// Sin este campo, "no hay comprobante" y "el comprobante falló" serían indistinguibles.
/// </remarks>
public enum ReceiptStatus
{
  Pending = 0,
  Available = 1,
  Failed = 2
}


/// <summary>
/// Una compra registrada: qué se compró, a cuánto y a quién se envía.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Los datos del producto y del cliente se COPIAN, no se referencian.</b> El precio,
/// el nombre y el SKU quedan congelados en el momento de comprar. Si mañana sube el precio
/// o se renombra el producto, el comprobante de ayer tiene que seguir diciendo lo que se
/// cobró de verdad — un documento que cambia cuando cambia el catálogo no sirve como
/// comprobante de nada.
/// </para>
/// <para>
/// Contexto acotado propio (<c>Ordering</c>) y no dentro de <c>Catalog</c>: tiene su propio
/// lenguaje —orden, línea, comprobante, envío— y sus propias invariantes. Que hoy solo
/// compre productos del catálogo no lo convierte en parte de él.
/// </para>
/// </remarks>
public class Order : IAuditable
{
  public int Id { get; set; }

  /// <summary>
  /// Número legible (<c>ORD-2026-000012</c>). Es lo que cita el cliente al escribir.
  /// </summary>
  /// <remarks>
  /// Columna real y con índice único, no una propiedad calculada: la gente busca por este
  /// número, y calcularlo en memoria obligaría a traer la tabla entera para encontrar uno.
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
  // Se guardan CALCULADOS y no se recalculan al leer: el desglose es parte del documento
  // y tiene que poder reproducirse aunque cambien los impuestos o las tarifas de envío.

  public decimal Subtotal { get; set; }
  public decimal Discount { get; set; }
  public decimal Tax { get; set; }
  public decimal Shipping { get; set; }
  public decimal Total { get; set; }

  // ---- copia de los datos del cliente -------------------------------------
  // También congelados: si el usuario cambia su email mañana, el comprobante emitido hoy
  // debe seguir mostrando al que se le envió.

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

  /// <summary>
  /// Clave <b>opaca</b> del documento en el almacén. <c>null</c> mientras no exista.
  /// </summary>
  /// <remarks>
  /// ⚠️ Aquí NO va una ruta del disco. Guardar <c>/app/documents/2026/09/x.pdf</c> ataría
  /// la base a la infraestructura de hoy: el día que los comprobantes vivan en S3 habría
  /// que reescribir todas las filas. Lo que hay es una clave que solo entiende
  /// <c>IDocumentStore</c>, y por eso cambiar de almacén no toca la base.
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

  /// <summary>
  /// Referencia al producto, <b>solo informativa</b>.
  /// </summary>
  /// <remarks>
  /// Sin clave foránea a propósito: si un producto se borra, la orden y su comprobante
  /// tienen que sobrevivir. Una FK obligaría a elegir entre impedir el borrado o borrar
  /// la historia de compras, y las dos son peores.
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

  /// <summary>
  /// <c>UnitPrice × Quantity</c>, guardado.
  /// </summary>
  /// <remarks>
  /// Redundante a propósito: es lo que se imprimió. Recalcularlo al leer parece más
  /// limpio hasta el día que cambia la forma de redondear y todos los comprobantes
  /// antiguos empiezan a cuadrar mal por un céntimo.
  /// </remarks>
  public decimal LineTotal { get; set; }
}
