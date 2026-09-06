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
/// <para>
/// ⚠️ <b>No lleva precios ni totales.</b> Los pone el servidor a partir del catálogo: un
/// precio que viaja en el cuerpo es un precio que el cliente elige. El email tampoco: sale
/// del claim del token.
/// </para>
/// <para>
/// ⚠️ <b>`CustomerName` SÍ lo elige el cliente, y es una decisión, no un descuido.</b> Se
/// imprime tal cual en el comprobante, así que cualquiera puede emitirse uno a nombre de
/// otra persona. No hay impacto cruzado —solo lo descarga quien compró— pero significa que
/// <b>este documento no vale como prueba de identidad de nadie</b>: es un recibo de compra,
/// no una factura. Es un dato de envío («¿a nombre de quién va el paquete?»), que es
/// legítimamente del comprador. El día que esto emita facturas fiscales, el nombre tiene
/// que salir del perfil verificado del usuario y no del cuerpo.
/// </para>
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

  /// <summary>
  /// <c>pending</c>, <c>available</c> o <c>failed</c>.
  /// </summary>
  /// <remarks>
  /// ⚠️ Se expone el ESTADO y no la clave del documento. La clave es un detalle del
  /// almacén: publicarla ataría el contrato de la API a la infraestructura de hoy, y el
  /// día que sea S3 el cliente estaría leyendo una clave que ya no significa nada.
  /// El cliente mira esto para saber si ya puede descargar.
  /// </remarks>
  public string ReceiptStatus { get; set; } = string.Empty;

  public IReadOnlyList<OrderItemDto> Items { get; set; } = [];
}
