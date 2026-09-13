using System.Net;
using System.Net.Http.Json;
using System.Text;
using System.Text.Json;
using ApiEcommerce.Features.Payments.Ports;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.AspNetCore.TestHost;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Cobrar una orden de punta a punta: intento, webhook, orden pagada y comprobante.
/// </summary>
/// <remarks>
/// Con <see cref="FakePaymentGateway"/> en lugar de Stripe: lo que se prueba aquí es el
/// flujo y las garantías, no la integración con el proveedor. El host no tiene broker, así
/// que el <c>payment.captured</c> se queda en el outbox y el efecto se dispara llamando al
/// handler, que es la mitad determinista.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class PaymentsTests(ApiFactory factory) : IDisposable
{
  private readonly FakePaymentGateway _gateway = new();
  private WebApplicationFactory<Program>? _host;

  /// <summary>El host con la pasarela falsa puesta en lugar de la de Stripe.</summary>
  private WebApplicationFactory<Program> Host => _host ??= factory.WithWebHostBuilder(builder =>
      builder.ConfigureTestServices(services =>
      {
        services.RemoveAll<IPaymentGateway>();
        services.AddSingleton<IPaymentGateway>(_gateway);
      }));

  public void Dispose()
  {
    _host?.Dispose();
    GC.SuppressFinalize(this);
  }

  // ---- iniciar el pago -----------------------------------------------------

  [Fact]
  public async Task StartingAPaymentReturnsWhatTheClientNeedsToConfirmIt()
  {
    var (buyer, order) = await AnOrderAsync();

    var payment = await StartAsync(buyer, order.Id);

    Assert.StartsWith($"PAY-{DateTime.Now:yyyy}-", payment.GetProperty("reference").GetString());
    Assert.Equal("pending", payment.GetProperty("status").GetString());
    Assert.Equal("stripe", payment.GetProperty("provider").GetString());
    Assert.Equal(order.Total, payment.GetProperty("amount").GetDecimal());

    // Lo público es el publicId: la clave primaria no sale de la base.
    Assert.Equal(order.Id, payment.GetProperty("orderPublicId").GetGuid());
    Assert.NotEqual(Guid.Empty, payment.PublicId());
    Assert.False(payment.TryGetProperty("id", out _));
    Assert.False(payment.TryGetProperty("orderId", out _));

    // Sin el clientSecret el front no puede confirmar nada; y NO se persiste.
    Assert.False(string.IsNullOrWhiteSpace(payment.GetProperty("clientSecret").GetString()));
  }

  [Fact]
  public async Task TheOrderIsNotPaidJustBecauseAPaymentStarted()
  {
    // Lo que mueve la orden es el webhook, no haber llamado a nuestra propia API.
    var (buyer, order) = await AnOrderAsync();

    await StartAsync(buyer, order.Id);

    Assert.Equal("placed", await OrderStatusAsync(buyer, order.Id));
  }

  [Fact]
  public async Task TheSameIdempotencyKeyDoesNotCreateTwoIntentsInTheGateway()
  {
    var (buyer, order) = await AnOrderAsync();
    var key = Guid.NewGuid().ToString();

    var before = _gateway.CreatedIntents;

    var first = await StartAsync(buyer, order.Id, key);
    var second = await StartAsync(buyer, order.Id, key);

    Assert.Equal(first.GetProperty("reference").GetString(), second.GetProperty("reference").GetString());
    Assert.Equal(1, _gateway.CreatedIntents - before);
  }

  [Fact]
  public async Task PayingSomeoneElsesOrderIs404()
  {
    var (_, order) = await AnOrderAsync();

    using var otro = await factory.AsNewUserAsync();

    var response = await PostStartAsync(otro, order.Id);

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task WithoutTheOrderPublicIdTheBodyIsInvalid()
  {
    // Sin [Required] sobre un Guid? llegaría Guid.Empty y saldría un 404 engañoso.
    var (buyer, _) = await AnOrderAsync();

    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payment")
    {
      Content = JsonContent.Create(new { provider = "stripe" })
    };
    request.Headers.Add("Idempotency-Key", Guid.NewGuid().ToString());

    var response = await buyer.SendAsync(request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task AnUnknownProviderIs400AndSaysWhichOnesExist()
  {
    var (buyer, order) = await AnOrderAsync();

    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payment")
    {
      Content = JsonContent.Create(new { orderPublicId = order.Id, provider = "bitcoin" })
    };

    var response = await buyer.SendAsync(request);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Contains("Stripe", await response.Content.ReadAsStringAsync());
  }

  [Fact]
  public async Task AnOrderCannotHaveTwoLivePayments()
  {
    var (buyer, order) = await AnOrderAsync();

    await StartAsync(buyer, order.Id);

    // Clave distinta: no es un reintento, es un segundo cobro sobre la misma orden.
    var response = await PostStartAsync(buyer, order.Id);

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  // ---- el webhook ----------------------------------------------------------

  [Fact]
  public async Task AWebhookWithoutAValidSignatureIsRejectedAndChangesNothing()
  {
    var (buyer, order) = await AnOrderAsync();
    var payment = await StartAsync(buyer, order.Id);

    var response = await SendWebhookAsync(
        Captured(payment), signature: "firma-de-un-atacante");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("pending", await PaymentStatusAsync(buyer, payment));
    Assert.Equal("placed", await OrderStatusAsync(buyer, order.Id));
  }

  [Fact]
  public async Task ACapturedWebhookPaysThePayment()
  {
    var (buyer, order) = await AnOrderAsync();
    var payment = await StartAsync(buyer, order.Id);

    var response = await SendWebhookAsync(Captured(payment));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal("captured", await PaymentStatusAsync(buyer, payment));
  }

  [Fact]
  public async Task TheSameWebhookTwiceOnlyCountsOnce()
  {
    // Las pasarelas reenvían por diseño: el webhook es at-least-once.
    var (buyer, order) = await AnOrderAsync();
    var payment = await StartAsync(buyer, order.Id);

    var body = Captured(payment);

    Assert.Equal(HttpStatusCode.OK, (await SendWebhookAsync(body)).StatusCode);
    Assert.Equal(HttpStatusCode.OK, (await SendWebhookAsync(body)).StatusCode);

    Assert.Equal("captured", await PaymentStatusAsync(buyer, payment));
    Assert.Equal(1, await CapturedEventsFor(payment));
  }

  [Fact]
  public async Task AWebhookForAPaymentWeDoNotKnowIs200AndNot404()
  {
    // Un error haría que la pasarela reintentara para siempre algo que nunca va a existir.
    var response = await SendWebhookAsync(
        JsonSerializer.Serialize(new { eventId = $"evt_{Guid.NewGuid():N}", paymentId = "pi_desconocido", status = "captured" }));

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task AFailedWebhookLeavesTheOrderUnpaid()
  {
    var (buyer, order) = await AnOrderAsync();
    var payment = await StartAsync(buyer, order.Id);

    await SendWebhookAsync(JsonSerializer.Serialize(new
    {
      eventId = $"evt_{Guid.NewGuid():N}",
      paymentId = $"pi_{payment.GetProperty("reference").GetString()}",
      status = "failed"
    }));

    Assert.Equal("failed", await PaymentStatusAsync(buyer, payment));
    Assert.Equal("placed", await OrderStatusAsync(buyer, order.Id));
  }

  // ---- consultar -----------------------------------------------------------

  [Fact]
  public async Task IOnlySeeMyOwnPayments()
  {
    var (buyer, order) = await AnOrderAsync();
    var payment = await StartAsync(buyer, order.Id);

    using var otro = await factory.AsNewUserAsync();

    var page = await otro.GetFromJsonAsync<JsonElement>("/api/v1/payment/paged?pageSize=100");

    Assert.Equal(0, page.GetProperty("totalItems").GetInt32());

    var mine = await buyer.GetFromJsonAsync<JsonElement>("/api/v1/payment/paged?pageSize=100");

    Assert.Contains(
        mine.GetProperty("items").EnumerateArray(),
        p => p.PublicId() == payment.PublicId());
  }

  [Fact]
  public async Task TheAdminListingRejectsARegularUser()
  {
    using var user = await factory.AsNewUserAsync();

    Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync("/api/v1/payment/all")).StatusCode);
  }

  // ---- helpers -------------------------------------------------------------

  private async Task<(HttpClient Buyer, (Guid Id, decimal Total) Order)> AnOrderAsync()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    var buyer = Host.CreateClient();

    await RegisterAsync(buyer);

    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/order")
    {
      Content = JsonContent.Create(new
      {
        items = new[] { new { sku, quantity = 1 } },
        customerName = "Cliente de prueba"
      })
    };

    var response = await buyer.SendAsync(request);

    response.EnsureSuccessStatusCode();

    var order = await response.Content.ReadFromJsonAsync<JsonElement>();

    return (buyer, (order.PublicId(), order.GetProperty("total").GetDecimal()));
  }

  private static async Task RegisterAsync(HttpClient client)
  {
    var username = $"pay{Guid.NewGuid():N}"[..20];

    var response = await client.PostAsJsonAsync("/api/v1/auth/register", new
    {
      username,
      email = $"{username}@apiecommerce.test",
      password = "Passw0rd!x",
      name = "Comprador"
    });

    response.EnsureSuccessStatusCode();

    var body = await response.Content.ReadFromJsonAsync<JsonElement>();

    client.DefaultRequestHeaders.Authorization = new("Bearer", body.GetProperty("token").GetString());
  }

  private async Task<JsonElement> StartAsync(HttpClient buyer, Guid orderId, string? key = null)
  {
    var response = await PostStartAsync(buyer, orderId, key);

    response.EnsureSuccessStatusCode();

    return await response.Content.ReadFromJsonAsync<JsonElement>();
  }

  private async Task<HttpResponseMessage> PostStartAsync(
      HttpClient buyer, Guid orderId, string? key = null)
  {
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payment")
    {
      Content = JsonContent.Create(new { orderPublicId = orderId, provider = "stripe" })
    };

    request.Headers.Add("Idempotency-Key", key ?? Guid.NewGuid().ToString());

    return await buyer.SendAsync(request);
  }

  private static string Captured(JsonElement payment) => JsonSerializer.Serialize(new
  {
    eventId = $"evt_{payment.GetProperty("reference").GetString()}",
    paymentId = $"pi_{payment.GetProperty("reference").GetString()}",
    status = "captured"
  });

  private async Task<HttpResponseMessage> SendWebhookAsync(
      string body, string signature = FakePaymentGateway.ValidSignature)
  {
    using var client = Host.CreateClient();
    using var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/payment/webhook/stripe")
    {
      Content = new StringContent(body, Encoding.UTF8, "application/json")
    };

    request.Headers.Add("Stripe-Signature", signature);

    return await client.SendAsync(request);
  }

  private static async Task<string?> PaymentStatusAsync(HttpClient buyer, JsonElement payment)
  {
    return (await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/payment/{payment.PublicId()}"))
        .GetProperty("status").GetString();
  }

  private static async Task<string?> OrderStatusAsync(HttpClient buyer, Guid orderId)
      => (await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/order/{orderId}"))
          .GetProperty("status").GetString();

  private async Task<int> CapturedEventsFor(JsonElement payment)
  {
    using var scope = Host.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<ApiEcommerce.Data.AppDbContext>();
    var reference = payment.GetProperty("reference").GetString();

    return await Microsoft.EntityFrameworkCore.EntityFrameworkQueryableExtensions.CountAsync(
        db.OutboxMessages.Where(m => m.Type == "payment.captured" && m.Payload.Contains(reference!)));
  }
}
