using ApiEcommerce.Features.Ordering.Dtos;
using ApiEcommerce.Features.Ordering.Ports;
using ApiEcommerce.Features.Ordering.Service;
using Moq;

namespace ApiEcommerce.Tests.Features.Ordering;


/// <summary>
/// Cotizar el carrito: precios y stock de este instante, sin comprometer nada.
/// </summary>
public class CartServiceTests
{
  private readonly Mock<ICatalogGateway> _catalog = new();

  private CartService Sut() => new(_catalog.Object);

  private void Sells(string sku, decimal price, int stock, int productId = 1)
      => _catalog.Setup(c => c.PeekAsync(sku, It.IsAny<CancellationToken>()))
                 .ReturnsAsync(new QuotableItem(productId, sku, $"Producto {sku}", null, price, stock, IsActive: true));

  private static QuoteCartDto Cart(params (string Sku, int Quantity)[] lines) => new()
  {
    Items = [.. lines.Select(l => new OrderLineDto { Sku = l.Sku, Quantity = l.Quantity })]
  };

  // ---- lo que NO debe hacer -----------------------------------------------

  [Fact]
  public async Task QuoteAsync_NeverReservesStock()
  {
    // Cotizar apartando stock dejaría el catálogo a cero con los carritos abandonados.
    Sells("SKU-1", 10m, stock: 5);

    await Sut().QuoteAsync(Cart(("SKU-1", 5)));

    _catalog.Verify(
        c => c.TryTakeAsync(It.IsAny<string>(), It.IsAny<int>(), It.IsAny<CancellationToken>()),
        Times.Never);
  }

  // ---- líneas que no se pueden servir --------------------------------------

  [Fact]
  public async Task QuoteAsync_WithoutEnoughStock_MarksTheLineAndKeepsGoing()
  {
    // 200 y no 409: el front tiene que poder enseñar "solo quedan 2" y dejar ajustar.
    Sells("SKU-1", 10m, stock: 2);

    var quote = await Sut().QuoteAsync(Cart(("SKU-1", 10)));

    var line = Assert.Single(quote.Items);
    Assert.Equal(CartLineStatus.InsufficientStock, line.Status);
    Assert.Equal(2, line.Available);
    Assert.Equal(2, line.MaxQuantity);
    Assert.False(quote.AllAvailable);
  }

  [Fact]
  public async Task QuoteAsync_WithAnUnknownSku_MarksItInsteadOfThrowing()
  {
    _catalog.Setup(c => c.PeekAsync("SKU-X", It.IsAny<CancellationToken>()))
            .ReturnsAsync((QuotableItem?)null);

    var line = Assert.Single((await Sut().QuoteAsync(Cart(("SKU-X", 1)))).Items);

    Assert.Equal(CartLineStatus.NotFound, line.Status);
    Assert.Null(line.ProductId);
  }

  [Fact]
  public async Task QuoteAsync_DoesNotChargeForWhatItCannotServe()
  {
    Sells("SKU-1", 10m, stock: 100);
    Sells("SKU-2", 50m, stock: 0, productId: 2);

    var quote = await Sut().QuoteAsync(Cart(("SKU-1", 2), ("SKU-2", 1)));

    // 2 x 10, y la línea sin stock no suma.
    Assert.Equal(20m, quote.Total);
    Assert.False(quote.AllAvailable);
  }

  // ---- mismas reglas que al comprar ----------------------------------------

  [Fact]
  public async Task QuoteAsync_GroupsRepeatedSkus()
  {
    // Si no agrupa igual que BuildAsync, el carrito y la orden salen con distinto número
    // de líneas para el mismo contenido.
    Sells("SKU-1", 10m, stock: 100);

    var quote = await Sut().QuoteAsync(Cart(("SKU-1", 2), ("SKU-1", 3)));

    var line = Assert.Single(quote.Items);
    Assert.Equal(5, line.Quantity);
    Assert.Equal(50m, line.LineTotal);
  }

  [Fact]
  public async Task QuoteAsync_UsesTheSamePricingAsPlacingTheOrder()
  {
    // La razón de ser de OrderPricing: dos cálculos separados divergen en cuanto uno gane
    // un impuesto, y la diferencia solo aparece al cobrar.
    Sells("SKU-1", 12.34m, stock: 100);

    var quote = await Sut().QuoteAsync(Cart(("SKU-1", 3)));
    var asOrder = OrderPricing.For([12.34m * 3]);

    Assert.Equal(asOrder.Subtotal, quote.Subtotal);
    Assert.Equal(asOrder.Tax, quote.Tax);
    Assert.Equal(asOrder.Shipping, quote.Shipping);
    Assert.Equal(asOrder.Discount, quote.Discount);
    Assert.Equal(asOrder.Total, quote.Total);
    Assert.Equal(OrderPricing.Currency, quote.Currency);
  }

  [Fact]
  public async Task QuoteAsync_WithEverythingInStock_SaysSo()
  {
    Sells("SKU-1", 10m, stock: 100);

    var quote = await Sut().QuoteAsync(Cart(("SKU-1", 1)));

    Assert.True(quote.AllAvailable);
    Assert.Equal(CartLineStatus.Ok, Assert.Single(quote.Items).Status);
  }
}
