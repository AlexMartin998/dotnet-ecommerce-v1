using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Features.Ordering.Events;
using ApiEcommerce.Features.Ordering.Messaging;
using ApiEcommerce.Features.Ordering.Models;
using ApiEcommerce.Features.Ordering.Service;
using ApiEcommerce.Shared.Messaging;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// El lado de Ordering: qué pasa cuando llega un cobro, y qué pasa cuando no llega nunca.
/// </summary>
/// <remarks>
/// El host de tests no tiene broker, así que el efecto se invoca directamente
/// (<see cref="IOrderPaymentHandler"/>), que es la mitad determinista de «llega el evento y
/// la orden se paga».
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class OrderPaymentTests(ApiFactory factory)
{
  // ---- el cobro que llega --------------------------------------------------

  [Fact]
  public async Task ACapturedPaymentMovesTheOrderToPaidAndAnnouncesIt()
  {
    var (buyer, orderId, number) = await AnOrderAsync();

    await CaptureAsync(orderId);

    Assert.Equal("paid", await StatusAsync(buyer, orderId));

    // El comprobante cuelga de order.paid: sin este evento, una orden pagada se quedaría
    // sin PDF y sin nadie que lo intentara.
    Assert.Equal(1, await OrderPaidEventsFor(number));
  }

  [Fact]
  public async Task TheSameCaptureTwiceMovesTheOrderOnce()
  {
    // El broker entrega al menos una vez, y un replay desde la DLQ llega con otro id.
    var (buyer, orderId, number) = await AnOrderAsync();

    await CaptureAsync(orderId);
    await CaptureAsync(orderId);

    Assert.Equal("paid", await StatusAsync(buyer, orderId));

    // Que la transición sea condicional (Placed -> Paid) es lo que evita el segundo evento.
    Assert.Equal(1, await OrderPaidEventsFor(number));
  }

  [Fact]
  public async Task ACaptureDoesNotResurrectACancelledOrder()
  {
    // Un webhook que llega tarde, después de que el recolector cancelara la orden por
    // impago, no puede devolverla a la vida: el stock ya volvió al catálogo.
    var (buyer, orderId, number) = await AnOrderAsync();

    await SetStatusAsync(orderId, OrderStatus.Cancelled);

    await CaptureAsync(orderId);

    Assert.Equal("cancelled", await StatusAsync(buyer, orderId));
    Assert.Equal(0, await OrderPaidEventsFor(number));
  }

  // ---- el cobro que no llega nunca -----------------------------------------

  [Fact]
  public async Task AnAbandonedOrderIsCancelledAndItsStockGoesBack()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    var (buyer, orderId, _) = await AnOrderAsync(sku, quantity: 3);

    Assert.Equal(7, await StockOf(admin, sku));

    await BackdateAsync(orderId, TimeSpan.FromHours(2));

    Assert.Equal(1, await CollectAsync());

    Assert.Equal("cancelled", await StatusAsync(buyer, orderId));
    Assert.Equal(10, await StockOf(admin, sku));
  }

  [Fact]
  public async Task CollectingTwiceReturnsTheStockOnlyOnce()
  {
    // La devolución cuelga de la transición placed -> cancelled: si la orden ya no está
    // esperando pago, no hay nada que devolver. Es lo que hace idempotente al recolector.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    var (_, orderId, _) = await AnOrderAsync(sku, quantity: 2);

    await BackdateAsync(orderId, TimeSpan.FromHours(2));

    await CollectAsync();
    await CollectAsync();

    Assert.Equal(10, await StockOf(admin, sku));
  }

  [Fact]
  public async Task TheCollectorDoesNotTouchAPaidOrder()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    var (buyer, orderId, _) = await AnOrderAsync(sku, quantity: 4);

    await CaptureAsync(orderId);
    await BackdateAsync(orderId, TimeSpan.FromHours(2));

    await CollectAsync();

    Assert.Equal("paid", await StatusAsync(buyer, orderId));

    // Lo importante no es el estado: es que NO se devolvió stock de una compra cobrada.
    Assert.Equal(6, await StockOf(admin, sku));
  }

  [Fact]
  public async Task TheCollectorDoesNotTouchAFreshOrder()
  {
    var (buyer, orderId, _) = await AnOrderAsync();

    await CollectAsync();

    Assert.Equal("placed", await StatusAsync(buyer, orderId));
  }

  // ---- helpers -------------------------------------------------------------

  private async Task<(HttpClient Buyer, int OrderId, string Number)> AnOrderAsync(
      string? sku = null, int quantity = 1)
  {
    if (sku is null)
    {
      using var admin = await factory.AsAdminAsync();
      sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);
    }

    var buyer = await factory.AsNewUserAsync();

    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/order")
    {
      Content = JsonContent.Create(new
      {
        items = new[] { new { sku, quantity } },
        customerName = "Cliente de prueba"
      })
    };

    var response = await buyer.SendAsync(request);

    response.EnsureSuccessStatusCode();

    var order = await response.Content.ReadFromJsonAsync<JsonElement>();

    return (buyer, await factory.OrderIdAsync(order.PublicId()), order.GetProperty("number").GetString()!);
  }

  /// <summary>Entrega un <c>payment.captured</c> como lo entregaría el consumidor.</summary>
  /// <remarks>
  /// Por el inbox y no llamando al handler a pelo: el efecto encola el evento con
  /// <c>Add</c> sin <c>SaveChanges</c> —a propósito, para que marca y efecto se confirmen
  /// juntos—, así que sin la transacción del inbox no se persistiría nada. Es justo lo que
  /// pasó al escribir este test.
  /// </remarks>
  private async Task CaptureAsync(int orderId, Guid? messageId = null)
  {
    using var scope = factory.Services.CreateScope();

    var notice = new PaymentCapturedNotice(
        1, $"PAY-TEST-{orderId}", orderId, "irrelevante", 10m, "USD", DateTime.Now);

    await scope.ServiceProvider.GetRequiredService<IMessageInbox>().ProcessOnceAsync(
        messageId ?? Guid.NewGuid(),
        PaymentCapturedNotice.EventType,
        token => scope.ServiceProvider
            .GetRequiredService<IOrderPaymentHandler>().HandleAsync(notice, token));
  }

  private async Task<int> CollectAsync()
  {
    using var scope = factory.Services.CreateScope();

    return await scope.ServiceProvider
        .GetRequiredService<IAbandonedOrderCollector>().CollectAsync();
  }

  /// <summary>Envejece la orden para no tener que esperar la ventana de reserva.</summary>
  private async Task BackdateAsync(int orderId, TimeSpan by)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    // Fuera del árbol de expresión: dentro, EF no sabe traducir la resta y lanza.
    var when = DateTime.Now - by;

    await db.Orders
        .Where(o => o.Id == orderId)
        .ExecuteUpdateAsync(s => s.SetProperty(o => o.PlacedAt, when));
  }

  private async Task SetStatusAsync(int orderId, OrderStatus status)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    await db.Orders
        .Where(o => o.Id == orderId)
        .ExecuteUpdateAsync(s => s.SetProperty(o => o.Status, status));
  }

  /// <summary>Cuántos <c>order.paid</c> hay en el outbox para esa orden.</summary>
  /// <remarks>Se busca por número, que es único; el id se repite entre payloads distintos.</remarks>
  private async Task<int> OrderPaidEventsFor(string number)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    return await db.OutboxMessages
        .Where(m => m.Type == OrderPaid.EventType && m.Payload.Contains(number))
        .CountAsync();
  }

  private async Task<string?> StatusAsync(HttpClient buyer, int orderId)
      => (await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/order/{await factory.OrderPublicIdAsync(orderId)}"))
          .GetProperty("status").GetString();

  private static Task<int> StockOf(HttpClient admin, string sku)
      => IdempotencyTests.StockOf(admin, sku);
}
