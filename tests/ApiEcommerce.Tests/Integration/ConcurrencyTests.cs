using System.Net;
using System.Net.Http.Json;
using ApiEcommerce.Shared.Idempotency;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Los tres bugs de carrera que este proyecto sufrió de verdad, convertidos en tests.
/// </summary>
/// <remarks>
/// ⚠️ <b>Con <c>Task.WhenAll</c> de peticiones reales, nunca en secuencia.</b> Es la
/// única forma de que valgan: en secuencia estos mismos escenarios pasaban también con
/// la implementación defectuosa, y por eso el bug del stock sobrevivió tanto tiempo. Un
/// test de concurrencia secuencial es un test que da falsa tranquilidad.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ConcurrencyTests(ApiFactory factory)
{
  [Fact]
  public async Task FifteenSimultaneousPurchasesOverStockTenNeverOversell()
  {
    // La medición original: 10×200, 5×409 y stock 0.
    //
    // Antes se implementó con RowVersion + reintentos y se MIDIÓ que no servía: no
    // sobrevendía, pero rechazaba compras válidas (5×200 + 5×409 sobre stock 10) al
    // agotar los reintentos. La concurrencia optimista sirve para EDITAR una entidad,
    // no para un contador con contención. Hoy es un UPDATE condicional atómico.
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
    // La regla aplicativa (CategoryRules.NameExistsAsync) comprueba antes de escribir,
    // pero entre esa comprobación y el INSERT cabe otra petición: los ocho pasaban la
    // validación. Quien de verdad garantiza la unicidad es el ÍNDICE ÚNICO en la base,
    // y el handler traduce ese choque a 409. La regla solo da el mensaje bonito.
    using var admin = await factory.AsAdminAsync();
    var name = VersioningAndHealthTests.Unique("Race");

    var responses = await Task.WhenAll(Enumerable.Range(0, 8)
        .Select(_ => admin.PostAsJsonAsync("/api/v1/category", new { name })));

    Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
    Assert.Equal(7, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));

    // Y ni un 500: el SqlException 2601/2627 tiene que llegar traducido.
    Assert.DoesNotContain(responses, r => (int)r.StatusCode >= 500);

    var listing = await admin.GetFromJsonAsync<System.Text.Json.JsonElement>(
        "/api/v1/category?", CancellationToken.None);

    Assert.Equal(1, listing.EnumerateArray().Count(c => c.GetProperty("name").GetString() == name));
  }

  [Fact]
  public async Task SixConcurrentPurchasesWithTheSameKeyBuyOnlyOnce()
  {
    // Doble submit real: el usuario pulsa el botón seis veces antes de que responda la
    // primera. La reserva atómica (SET NX en Redis) es lo que lo corta.
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

    // Las que llegan mientras la primera sigue en curso reciben 409
    // `idempotency_in_progress`; las que llegan después reproducen la respuesta con 200.
    // Lo que NO puede pasar es que se compre más de una vez.
    Assert.All(responses, r => Assert.True(
        r.StatusCode is HttpStatusCode.OK or HttpStatusCode.Conflict,
        $"esperaba 200 o 409, llegó {(int)r.StatusCode}"));

    Assert.Equal(8, await IdempotencyTests.StockOf(admin, sku));
  }
}
