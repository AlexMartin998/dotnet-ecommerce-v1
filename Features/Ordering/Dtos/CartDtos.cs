using System.ComponentModel.DataAnnotations;

namespace ApiEcommerce.Features.Ordering.Dtos;


/// <summary>Lo que el cliente manda para cotizar un carrito.</summary>
/// <remarks>
/// Solo SKU y cantidad, igual que al comprar: si el cliente mandara precios habría que
/// validarlos, y un cambio legítimo de precio rompería el carrito.
/// </remarks>
public class QuoteCartDto
{
  [Required]
  [MinLength(1)]
  [MaxLength(50)]
  public List<OrderLineDto> Items { get; set; } = [];
}


/// <summary>Por qué una línea del carrito no se puede comprar tal cual.</summary>
public static class CartLineStatus
{
  /// <summary>Hay stock para la cantidad pedida.</summary>
  public const string Ok = "ok";

  /// <summary>El SKU existe pero no hay tantas unidades.</summary>
  public const string InsufficientStock = "insufficient_stock";

  /// <summary>Ningún producto responde a ese SKU.</summary>
  public const string NotFound = "not_found";
}


/// <summary>Una línea cotizada, con el precio y el stock de este instante.</summary>
public class CartLineDto
{
  public string Sku { get; set; } = string.Empty;

  /// <summary>Nulo cuando el SKU no existe.</summary>
  public int? ProductId { get; set; }

  public string? Name { get; set; }

  public decimal UnitPrice { get; set; }

  /// <summary>Lo que pidió el cliente, no lo que se puede servir.</summary>
  public int Quantity { get; set; }

  public decimal LineTotal { get; set; }

  /// <summary>Unidades en catálogo ahora mismo.</summary>
  public int Available { get; set; }

  /// <summary>Lo máximo que el front debería dejar pedir de esta línea.</summary>
  public int MaxQuantity { get; set; }

  /// <summary>Uno de <see cref="CartLineStatus"/>.</summary>
  public string Status { get; set; } = CartLineStatus.Ok;
}


/// <summary>El carrito cotizado por el servidor.</summary>
/// <remarks>
/// Es una foto, no una reserva: entre cotizar y comprar el precio y el stock pueden cambiar,
/// y manda lo que diga el checkout. Cotizar no aparta nada.
/// </remarks>
public class CartQuoteDto
{
  public IReadOnlyList<CartLineDto> Items { get; set; } = [];

  /// <summary>Falso si alguna línea no se puede servir: el checkout daría 409.</summary>
  public bool AllAvailable { get; set; }

  public string Currency { get; set; } = string.Empty;

  /// <summary>Suma de las líneas disponibles. Las demás no cuentan.</summary>
  public decimal Subtotal { get; set; }

  public decimal Discount { get; set; }

  public decimal Tax { get; set; }

  public decimal Shipping { get; set; }

  public decimal Total { get; set; }
}
