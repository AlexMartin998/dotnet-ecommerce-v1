using System.ComponentModel.DataAnnotations;
using ApiEcommerce.Features.Ordering.Models;

namespace ApiEcommerce.Features.Ordering.Dtos;


/// <summary>Una línea del carrito.</summary>
public class OrderLineDto
{
  [Required]
  [MaxLength(50)]
  public string Sku { get; set; } = string.Empty;

  [Range(1, 1000)]
  public int Quantity { get; set; }
}


/// <summary>Lo que manda el cliente para cerrar una compra.</summary>
/// <remarks>
/// No lleva precios ni totales: los pone el servidor desde el catálogo, y el email sale del
/// token. <c>CustomerName</c> sí es del cliente porque es un dato de envío, y por eso el
/// comprobante no vale como prueba de identidad.
/// </remarks>
public class PlaceOrderDto
{
  [Required]
  [MinLength(1)]
  [MaxLength(50)]
  public List<OrderLineDto> Items { get; set; } = [];

  [Required]
  [MaxLength(200)]
  public string CustomerName { get; set; } = string.Empty;

  [MaxLength(64)]
  public string? CustomerPhone { get; set; }

  [MaxLength(500)]
  public string? ShippingAddress { get; set; }
}


/// <summary>Una línea, ya con el precio congelado.</summary>
public class OrderItemDto
{
  public int ProductId { get; set; }
  public string Sku { get; set; } = string.Empty;
  public string Name { get; set; } = string.Empty;
  public decimal UnitPrice { get; set; }
  public int Quantity { get; set; }
  public decimal LineTotal { get; set; }
}


/// <summary>Una orden, como la ve su comprador.</summary>
public class OrderDto
{
  public int Id { get; set; }
  public string Number { get; set; } = string.Empty;
  public string Status { get; set; } = string.Empty;
  public string Currency { get; set; } = string.Empty;

  public decimal Subtotal { get; set; }
  public decimal Discount { get; set; }
  public decimal Tax { get; set; }
  public decimal Shipping { get; set; }
  public decimal Total { get; set; }

  public string CustomerName { get; set; } = string.Empty;
  public string? CustomerEmail { get; set; }
  public string? CustomerPhone { get; set; }
  public string? ShippingAddress { get; set; }

  public DateTime PlacedAt { get; set; }

  /// <summary>Si el comprobante ya se puede descargar: <c>pending</c>, <c>available</c> o <c>failed</c>.</summary>
  /// <remarks>
  /// Se expone el estado y no la clave del documento, que es un detalle del almacén y
  /// ataría el contrato de la API a la infraestructura de hoy.
  /// </remarks>
  public string ReceiptStatus { get; set; } = string.Empty;

  public IReadOnlyList<OrderItemDto> Items { get; set; } = [];
}
