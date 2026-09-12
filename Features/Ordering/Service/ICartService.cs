using ApiEcommerce.Features.Ordering.Dtos;

namespace ApiEcommerce.Features.Ordering.Service;


/// <summary>Cotiza un carrito contra el catálogo, sin comprometer nada.</summary>
public interface ICartService
{
  /// <summary>
  /// Devuelve cada línea con el precio y el stock actuales, más el desglose de la compra.
  /// </summary>
  /// <remarks>
  /// Nunca lanza por falta de stock: las líneas que no se pueden servir vuelven marcadas,
  /// porque el front tiene que poder enseñarlas y dejar ajustar la cantidad.
  /// </remarks>
  Task<CartQuoteDto> QuoteAsync(QuoteCartDto dto, CancellationToken ct = default);
}
