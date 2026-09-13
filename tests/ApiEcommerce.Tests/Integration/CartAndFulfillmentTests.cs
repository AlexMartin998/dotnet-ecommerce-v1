using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Features.Ordering.Models;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Cotizar el carrito, mover la orden por su ciclo de entrega y los contadores del panel,
/// contra la API entera.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class CartAndFulfillmentTests(ApiFactory factory)
{
  // ---- cotizar el carrito --------------------------------------------------

  [Fact]
  public async Task QuotingIsAnonymous()
  {
    // El carrito existe antes que la sesión: pedir la cuenta para ver el precio de lo que
    // ya se está mirando es poner la cuenta por delante del producto.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var anonymous = factory.Anonymous();
    var response = await anonymous.PostAsJsonAsync("/api/v1/cart/quote", Cart((sku, 2)));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task QuotingDoesNotReserveStock()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var anonymous = factory.Anonymous();
    await anonymous.PostAsJsonAsync("/api/v1/cart/quote", Cart((sku, 5)));
    await anonymous.PostAsJsonAsync("/api/v1/cart/quote", Cart((sku, 5)));

    Assert.Equal(5, await StockOf(sku));
  }

  [Fact]
  public async Task QuotingAndPlacingAgreeOnTheTotal()
  {
    // Es el test que protege la razón de existir de OrderPricing: si alguien añade un
    // impuesto en un solo sitio, esto cae.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var buyer = await factory.AsNewUserAsync();

    var quote = await (await buyer.PostAsJsonAsync("/api/v1/cart/quote", Cart((sku, 3))))
        .Content.ReadFromJsonAsync<JsonElement>();

    var order = await PlaceAsync(buyer, (sku, 3));

    Assert.Equal(
        quote.GetProperty("total").GetDecimal(),
        order.GetProperty("total").GetDecimal());
    Assert.Equal(
        quote.GetProperty("currency").GetString(),
        order.GetProperty("currency").GetString());
  }

  [Fact]
  public async Task AnUnservableLineComesBackMarkedWithA200()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 2);

    using var anonymous = factory.Anonymous();
    var response = await anonymous.PostAsJsonAsync("/api/v1/cart/quote", Cart((sku, 10)));

    // 200 y no 409: el front tiene que poder enseñar "solo quedan 2".
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var quote = await response.Content.ReadFromJsonAsync<JsonElement>();
    var line = quote.GetProperty("items")[0];

    Assert.Equal("insufficient_stock", line.GetProperty("status").GetString());
    Assert.Equal(2, line.GetProperty("available").GetInt32());
    Assert.False(quote.GetProperty("allAvailable").GetBoolean());
    Assert.Equal(0m, quote.GetProperty("total").GetDecimal());
  }

  // ---- mover la orden ------------------------------------------------------

  [Fact]
  public async Task OnlyAnAdminMovesAnOrder()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var buyer = await factory.AsNewUserAsync();
    var id = (await PlaceAsync(buyer, (sku, 1))).PublicId();

    var response = await buyer.PatchAsJsonAsync(
        $"/api/v1/order/{id}/status", new { status = "preparing" });

    // 403 y no 404: la orden es suya, lo que no tiene es el rol.
    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Fact]
  public async Task AnUnpaidOrderCannotBePrepared()
  {
    // Preparar un pedido que nadie ha pagado es enviar mercancía gratis.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var buyer = await factory.AsNewUserAsync();
    var id = (await PlaceAsync(buyer, (sku, 1))).PublicId();

    var response = await admin.PatchAsJsonAsync(
        $"/api/v1/order/{id}/status", new { status = "preparing" });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("invalid_transition", problem.GetProperty("code").GetString());
  }

  [Fact]
  public async Task ThePaidOrderWalksTheWholePath()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var buyer = await factory.AsNewUserAsync();
    var id = (await PlaceAsync(buyer, (sku, 1))).PublicId();
    await MarkPaidAsync(id);

    foreach (var step in new[] { "preparing", "shipped", "delivered" })
    {
      var response = await admin.PatchAsJsonAsync($"/api/v1/order/{id}/status", new { status = step });

      Assert.Equal(HttpStatusCode.NoContent, response.StatusCode);
      Assert.Equal(step, await StatusOf(buyer, id));
    }

    // Entregada es final.
    var beyond = await admin.PatchAsJsonAsync(
        $"/api/v1/order/{id}/status", new { status = "shipped" });

    Assert.Equal(HttpStatusCode.Conflict, beyond.StatusCode);
  }

  [Fact]
  public async Task RepeatingATransitionIsHarmless()
  {
    // La transición es un UPDATE condicional: reenviarla es un duplicado, no un error.
    // Por eso este endpoint no necesita Idempotency-Key.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var buyer = await factory.AsNewUserAsync();
    var id = (await PlaceAsync(buyer, (sku, 1))).PublicId();
    await MarkPaidAsync(id);

    await admin.PatchAsJsonAsync($"/api/v1/order/{id}/status", new { status = "preparing" });
    var again = await admin.PatchAsJsonAsync($"/api/v1/order/{id}/status", new { status = "preparing" });

    Assert.Equal(HttpStatusCode.NoContent, again.StatusCode);
    Assert.Equal("preparing", await StatusOf(buyer, id));
  }

  [Fact]
  public async Task TwoAdminsMovingTheSameOrderAtOnceLeaveItMovedExactlyOnce()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var buyer = await factory.AsNewUserAsync();
    var id = (await PlaceAsync(buyer, (sku, 1))).PublicId();
    await MarkPaidAsync(id);

    // Simultáneas, no en fila: es la única forma de que aparezca un read-then-write.
    var responses = await Task.WhenAll(Enumerable.Range(0, 8).Select(_ =>
        admin.PatchAsJsonAsync($"/api/v1/order/{id}/status", new { status = "preparing" })));

    // Una la movió y las demás son reenvíos: ninguna puede fallar ni dejar otro estado.
    Assert.All(responses, r => Assert.Equal(HttpStatusCode.NoContent, r.StatusCode));
    Assert.Equal("preparing", await StatusOf(buyer, id));

    foreach (var response in responses) response.Dispose();
  }

  [Fact]
  public async Task AnUnreachableStatusIsRejectedAndListsTheValidOnes()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var buyer = await factory.AsNewUserAsync();
    var id = (await PlaceAsync(buyer, (sku, 1))).PublicId();

    // `paid` lo mueve un cobro capturado, nunca un administrador.
    var response = await admin.PatchAsJsonAsync(
        $"/api/v1/order/{id}/status", new { status = "paid" });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Contains("preparing", problem.GetProperty("detail").GetString());
  }

  // ---- los contadores del panel --------------------------------------------

  [Theory]
  [InlineData("/api/v1/order/stats")]
  [InlineData("/api/v1/product/stats")]
  [InlineData("/api/v1/user/stats")]
  public async Task StatsAreAdminOnly(string route)
  {
    using var buyer = await factory.AsNewUserAsync();

    Assert.Equal(HttpStatusCode.Forbidden, (await buyer.GetAsync(route)).StatusCode);
  }

  [Fact]
  public async Task LowStockDoesNotIncludeWhatIsAlreadyGone()
  {
    // Mezclarlos hace que el panel pida reponer lo que ya no se puede vender.
    using var admin = await factory.AsAdminAsync();
    await AuthorizationTests.CreateProductAsync(admin, stock: 0);
    await AuthorizationTests.CreateProductAsync(admin, stock: 3);

    var stats = await admin.GetFromJsonAsync<JsonElement>("/api/v1/product/stats");

    Assert.True(stats.GetProperty("outOfStock").GetInt32() >= 1);
    Assert.True(stats.GetProperty("lowStock").GetInt32() >= 1);
    Assert.Equal(10, stats.GetProperty("lowStockThreshold").GetInt32());
  }

  [Fact]
  public async Task OrderStatsCountEveryStateEvenWithNoRows()
  {
    using var admin = await factory.AsAdminAsync();

    var stats = await admin.GetFromJsonAsync<JsonElement>("/api/v1/order/stats");

    // Presentes aunque valgan 0: un hueco en el panel se lee como un fallo.
    foreach (var state in new[] { "placed", "paid", "preparing", "shipped", "delivered", "cancelled" })
      Assert.True(stats.TryGetProperty(state, out _), state);
  }

  [Fact]
  public async Task UserStatsCountTheSeededAdmin()
  {
    using var admin = await factory.AsAdminAsync();

    var stats = await admin.GetFromJsonAsync<JsonElement>("/api/v1/user/stats");

    Assert.True(stats.GetProperty("total").GetInt32() >= 1);
    Assert.True(stats.GetProperty("admins").GetInt32() >= 1);
  }

  // ---- helpers -------------------------------------------------------------

  private static object Cart(params (string Sku, int Quantity)[] lines) => new
  {
    items = lines.Select(l => new { sku = l.Sku, quantity = l.Quantity })
  };

  private static async Task<JsonElement> PlaceAsync(
      HttpClient client, params (string Sku, int Quantity)[] lines)
  {
    var response = await client.PostAsJsonAsync("/api/v1/order", new
    {
      items = lines.Select(l => new { sku = l.Sku, quantity = l.Quantity }),
      customerName = "Cliente de prueba"
    });

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<JsonElement>();
  }

  private static async Task<string?> StatusOf(HttpClient client, Guid orderId)
      => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/order/{orderId}"))
          .GetProperty("status").GetString();

  /// <summary>Lee el stock de la base: `/product/search` busca por NOMBRE, no por SKU.</summary>
  private async Task<int> StockOf(string sku)
  {
    using var scope = factory.Services.CreateScope();

    return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
        .Products.AsNoTracking()
        .Where(p => p.SKU == sku)
        .Select(p => p.Stock)
        .FirstAsync();
  }

  /// <summary>
  /// Pone la orden en <c>paid</c> por el mismo camino que el cobro: la transición
  /// condicional del repositorio. Sin broker en el host de tests, es la mitad determinista.
  /// </summary>
  private async Task MarkPaidAsync(Guid orderId)
  {
    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var now = DateTime.Now;

    await db.Orders
        .Where(o => o.PublicId == orderId && o.Status == OrderStatus.Placed)
        .ExecuteUpdateAsync(setters => setters
            .SetProperty(o => o.Status, OrderStatus.Paid)
            .SetProperty(o => o.UpdatedAt, now));
  }
}
