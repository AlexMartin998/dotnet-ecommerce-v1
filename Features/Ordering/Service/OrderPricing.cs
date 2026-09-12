namespace ApiEcommerce.Features.Ordering.Service;


/// <summary>El desglose de dinero de un carrito o de una orden.</summary>
public readonly record struct OrderTotals(
    decimal Subtotal, decimal Discount, decimal Tax, decimal Shipping, decimal Total);


/// <summary>
/// La única pieza que convierte importes de línea en el desglose de una compra.
/// </summary>
/// <remarks>
/// Cotizar el carrito y colocar la orden tienen que dar el mismo total, así que el cálculo
/// no puede estar en los dos sitios: dos copias divergen en cuanto una gane un impuesto o
/// un descuento, y la diferencia solo aparece al cobrar.
/// </remarks>
public static class OrderPricing
{
  /// <summary>Moneda única mientras no haya catálogo multi-moneda.</summary>
  public const string Currency = "USD";

  /// <summary>
  /// Calcula el desglose de las líneas dadas. Hoy no hay impuestos, descuentos ni portes:
  /// el desglose existe igual para que añadirlos sea tocar solo este archivo.
  /// </summary>
  public static OrderTotals For(IEnumerable<decimal> lineTotals)
  {
    ArgumentNullException.ThrowIfNull(lineTotals);

    var subtotal = lineTotals.Sum();

    const decimal discount = 0m;
    const decimal tax = 0m;
    const decimal shipping = 0m;

    return new OrderTotals(subtotal, discount, tax, shipping, subtotal - discount + tax + shipping);
  }
}
