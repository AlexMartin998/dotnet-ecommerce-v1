using ApiEcommerce.Features.Ordering.Dtos;
using ApiEcommerce.Features.Ordering.Ports;

namespace ApiEcommerce.Features.Ordering.Service;


/// <inheritdoc cref="ICartService"/>
public sealed class CartService(ICatalogGateway catalog) : ICartService
{
  /// <summary>Tope por línea de <see cref="OrderLineDto.Quantity"/>, que el front también debe respetar.</summary>
  private const int MaxPerLine = 1000;

  /// <inheritdoc />
  public async Task<CartQuoteDto> QuoteAsync(QuoteCartDto dto, CancellationToken ct = default)
  {
    ArgumentNullException.ThrowIfNull(dto);

    // Se agrupan igual que al comprar, o la cotización y la orden saldrían con distinto
    // número de líneas para el mismo carrito.
    var requested = dto.Items
        .GroupBy(i => i.Sku.Trim(), StringComparer.OrdinalIgnoreCase)
        .Select(g => (Sku: g.Key, Quantity: g.Sum(i => i.Quantity)))
        .OrderBy(line => line.Sku, StringComparer.OrdinalIgnoreCase)
        .ToList();

    var lines = new List<CartLineDto>(requested.Count);

    foreach (var (sku, quantity) in requested)
      lines.Add(await QuoteLineAsync(sku, quantity, ct));

    // Solo suman las líneas servibles: cobrar por lo que no hay sería el peor total posible.
    var totals = OrderPricing.For(
        lines.Where(l => l.Status == CartLineStatus.Ok).Select(l => l.LineTotal));

    return new CartQuoteDto
    {
      Items = lines,
      AllAvailable = lines.TrueForAll(l => l.Status == CartLineStatus.Ok),
      Currency = OrderPricing.Currency,
      Subtotal = totals.Subtotal,
      Discount = totals.Discount,
      Tax = totals.Tax,
      Shipping = totals.Shipping,
      Total = totals.Total
    };
  }

  private async Task<CartLineDto> QuoteLineAsync(string sku, int quantity, CancellationToken ct)
  {
    var item = await catalog.PeekAsync(sku, ct);

    if (item is null)
      return new CartLineDto { Sku = sku, Quantity = quantity, Status = CartLineStatus.NotFound };

    var found = item.Value;

    var status = found.Stock >= quantity
        ? CartLineStatus.Ok
        : CartLineStatus.InsufficientStock;

    return new CartLineDto
    {
      Sku = found.Sku,
      ProductId = found.ProductId,
      Name = found.Name,
      UnitPrice = found.UnitPrice,
      Quantity = quantity,
      LineTotal = found.UnitPrice * quantity,
      Available = found.Stock,
      // Lo que el front debe dejar pedir: el stock, pero nunca por encima del tope del DTO.
      MaxQuantity = Math.Min(found.Stock, MaxPerLine),
      Status = status
    };
  }
}
