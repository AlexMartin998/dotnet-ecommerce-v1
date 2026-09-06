using System.Net;
using System.Net.Http.Json;
using ApiEcommerce.Shared.Idempotency;

namespace ApiEcommerce.Tests.Integration;


/// <summary>Las carreras que el proyecto tiene que soportar, con peticiones reales.</summary>
/// <remarks>
/// Siempre con <c>Task.WhenAll</c> y nunca en secuencia: en secuencia estos escenarios
/// pasan también con la implementación defectuosa.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ConcurrencyTests(ApiFactory factory)
{
  [Fact]
  public async Task FifteenSimultaneousPurchasesOverStockTenNeverOversell()
  {
    // Es un UPDATE condicional atómico y no concurrencia optimista: con RowVersion y
    // reintentos no sobrevende, pero rechaza compras válidas al agotar los reintentos.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();

    var responses = await Task.WhenAll(Enumerable.Range(0, 15)
        .Select(_ => user.PostAsJsonAsync("/api/v1/product/buy", new { sku, quantity = 1 })));

    Assert.Equal(10, responses.Count(r => r.StatusCode == HttpStatusCode.OK));
    Assert.Equal(5, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

    // Lo innegociable: ni una unidad de más.
    Assert.Equal(0, await IdempotencyTests.StockOf(admin, sku));
  }

  [Fact]
  public async Task EightSimultaneousCreatesOfTheSameNameLeaveExactlyOneRow()
  {
    // Entre la comprobación de la regla y el INSERT cabe otra petición: quien garantiza
    // la unicidad es el índice único, y la regla solo da el mensaje bonito.
    using var admin = await factory.AsAdminAsync();
    var name = VersioningAndHealthTests.Unique("Race");

    var responses = await Task.WhenAll(Enumerable.Range(0, 8)
        .Select(_ => admin.PostAsJsonAsync("/api/v1/category", new { name })));

    Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
    Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

    // Ni un 500: el SqlException 2601/2627 tiene que llegar traducido.
    Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);

    var listing = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>(
        "/api/v1/category?", CancellationToken.None);

    Assert.Equal(1, listing.EnumerateArray().Count(c => c.GetProperty("name").GetString() == name));
  }

  [Fact]
  public async Task SixConcurrentPurchasesWithTheSameKeyBuyOnlyOnce()
  {
    // Doble submit: el usuario pulsa seis veces antes de que responda la primera.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var responses = await Task.WhenAll(Enumerable.Range(0, 6).Select(_ =>
    {
      var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/product/buy")
      {
        Content = JsonContent.Create(new { sku, quantity = 2 })
      };
      request.Headers.Add(IdempotentAttribute.HeaderName, key);
      return user.SendAsync(request);
    }));

    // Las que llegan en curso reciben 409 y las posteriores reproducen la respuesta con
    // 200; lo que no puede pasar es que se compre más de una vez.
    Assert.All(responses, r => Assert.True(
        r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
        $"esperaba 200 o 409, llegó {(int)r.StatusCode}"));

    Assert.Equal(8, await IdempotencyTests.StockOf(admin, sku));
  }
}
