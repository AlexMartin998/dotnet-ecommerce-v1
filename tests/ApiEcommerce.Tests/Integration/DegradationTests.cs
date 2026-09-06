using System.Net;
using System.Net.Http.Json;
using ApiEcommerce.Shared.Idempotency;
using Microsoft.AspNetCore.Hosting;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Host con Redis apuntando a un puerto muerto. No hace falta parar nada: la app no
/// distingue "Redis caído" de "Redis inalcanzable", que es justo lo que se quiere probar.
/// </summary>
public sealed class RedisDownFactory : ApiFactory
{
  protected override IDictionary<string, string?> Overrides => new Dictionary<string, string?>
  {
    // Mismo host que el Redis real pero un puerto donde no escucha nadie: así funciona
    // igual en el dev container y en CI, sin depender de parar ningún servicio.
    ["Redis:Configuration"] = $"{Environment.GetEnvironmentVariable("TEST_REDIS_DOWN") ?? "172.17.0.1:6998"}"
  };
}


/// <summary>
/// Degradación: cache e idempotencia son optimizaciones, no dependencias duras.
/// </summary>
/// <remarks>
/// Todas las implementaciones tienen que fallar en abierto: mezclarlo devolvería un 500
/// por una compra ya cobrada. Con Redis caído se pierde el atajo, no la garantía, que
/// vive en <c>ExecutedCommands</c> dentro de la transacción del efecto.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class DegradationTests : IClassFixture<RedisDownFactory>
{
  private readonly RedisDownFactory _factory;

  public DegradationTests(RedisDownFactory factory) => _factory = factory;

  [Fact]
  public async Task WithRedisDownTheCatalogStillReads()
  {
    // El decorador de cache no puede convertir un fallo de infraestructura en un 500.
    using var client = _factory.Anonymous();

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync("/api/v1/category")).StatusCode);
  }

  [Fact]
  public async Task WithRedisDownTheWritesStillWork()
  {
    // La invalidación de cache tampoco puede tumbar una escritura válida.
    using var admin = await _factory.AsAdminAsync();

    var response = await admin.PostAsJsonAsync("/api/v1/category",
        new { name = VersioningAndHealthTests.Unique("Degrade") });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
  }

  [Fact]
  public async Task WithRedisDownAPurchaseWithAnIdempotencyKeyStillSucceeds()
  {
    // Sin idempotencia disponible la compra se ejecuta igual: no se puede devolver un 500
    // por una operación que la base ya confirmó.
    using var admin = await _factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var user = await _factory.AsNewUserAsync();

    var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/product/buy")
    {
      Content = JsonContent.Create(new { sku, quantity = 1 })
    };
    request.Headers.Add(IdempotentAttribute.HeaderName, Guid.NewGuid().ToString());

    var response = await user.SendAsync(request);

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task WithRedisDownTheGuaranteeStillHoldsAndTheRetryDoesNotBuyTwice()
  {
    // La marca se escribe en la misma transacción que el descuento de stock, así que la
    // garantía no depende de Redis: sin base no hay compra, y con base hay marca.
    using var admin = await _factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await _factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var first = await Buy(user, sku, 3, key);
    var second = await Buy(user, sku, 3, key);

    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.Equal(HttpStatusCode.OK, second.StatusCode);

    // Lo que importa: 10 - 3, no 10 - 6.
    Assert.Equal(7, await IdempotencyTests.StockOf(admin, sku));

    // La cabecera de replay sale de lo que reporta el servicio, no del atajo de Redis.
    Assert.Equal("true", second.Headers.GetValues(IdempotentAttribute.ReplayedHeader).Single());
  }

  [Fact]
  public async Task WithRedisDownConcurrentRequestsWithTheSameKeyStillBuyOnce()
  {
    // Sin el atajo arbitra la clave primaria de ExecutedCommands: el perdedor choca, su
    // transacción se deshace entera y devuelve el resultado del ganador. De ahí que no
    // haya ningún 409 aquí.
    using var admin = await _factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 20);

    using var user = await _factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var responses = await Task.WhenAll(
        Enumerable.Range(0, 6).Select(_ => Buy(user, sku, 2, key)));

    Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));
    Assert.Equal(18, await IdempotencyTests.StockOf(admin, sku));

    // Exactamente una ejecutó de verdad; el resto son replays.
    Assert.Equal(1, responses.Count(r => !r.Headers.Contains(IdempotentAttribute.ReplayedHeader)));

    foreach (var response in responses) response.Dispose();
  }

  [Fact]
  public async Task WithRedisDownReusingTheKeyWithADifferentBodyIsStill422()
  {
    // La huella se comprueba en la transacción y lo señala una excepción de dominio, así
    // que el 422 no depende del filtro HTTP.
    using var admin = await _factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await _factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    await Buy(user, sku, 2, key);
    var mismatched = await Buy(user, sku, 5, key);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, mismatched.StatusCode);

    var problem = await mismatched.Content.ReadFromJsonAsync<System.Text.Json.JsonElement>();
    Assert.Equal("idempotency_key_reuse", problem.GetProperty("code").GetString());

    // Y la segunda no se ejecutó: 10 - 2, no 10 - 7.
    Assert.Equal(8, await IdempotencyTests.StockOf(admin, sku));
  }

  private static Task<HttpResponseMessage> Buy(HttpClient client, string sku, int quantity, string key)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/product/buy")
    {
      Content = JsonContent.Create(new { sku, quantity })
    };

    request.Headers.Add(IdempotentAttribute.HeaderName, key);

    return client.SendAsync(request);
  }

  [Fact]
  public async Task WithoutABrokerThePurchaseCompletesAndTheEventWaitsInTheOutbox()
  {
    // Escribir el evento es parte de la transacción de negocio y no puede depender de que
    // haya broker: la compra se completa y el evento espera en OutboxMessages.
    using var admin = await _factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var user = await _factory.AsNewUserAsync();
    var response = await user.PostAsJsonAsync("/api/v1/product/buy", new { sku, quantity = 1 });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(4, await IdempotencyTests.StockOf(admin, sku));
  }
}
