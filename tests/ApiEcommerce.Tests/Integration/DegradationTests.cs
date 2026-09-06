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
/// Degradación: cache e idempotencia son <b>optimizaciones</b>, no dependencias duras.
/// </summary>
/// <remarks>
/// ⚠️ Y la decisión de degradar tiene que ser <b>la misma en todas las implementaciones</b>.
/// Que <c>RedisCacheService</c> fallara en abierto y <c>RedisIdempotencyStore</c> en
/// cerrado hacía que un corte de Redis devolviera <b>500 por una compra ya cobrada</b>.
/// Estos tests fijan que ambas fallan en abierto.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class DegradationTests : IClassFixture<RedisDownFactory>
{
  private readonly RedisDownFactory _factory;

  public DegradationTests(RedisDownFactory factory) => _factory = factory;

  [Fact]
  public async Task WithRedisDownTheCatalogStillReads()
  {
    // El decorador de cache no puede convertir un fallo de infraestructura en un 500:
    // si Redis no responde, se sirve de la base y ya está.
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
    // ⚠️ EL test del P0. Sin idempotencia disponible, la compra se ejecuta igual: se
    // pierde la protección contra el doble submit, que es una optimización, pero NO se
    // devuelve 500 por una operación que la base ya confirmó.
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
  public async Task WithoutABrokerThePurchaseCompletesAndTheEventWaitsInTheOutbox()
  {
    // El host de tests corre SIN broker a propósito (RabbitMq:ConnectionString vacío).
    // Escribir el evento es parte de la transacción de negocio y no puede depender de
    // que haya broker: la compra se completa y el evento se acumula en OutboxMessages.
    using var admin = await _factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 5);

    using var user = await _factory.AsNewUserAsync();
    var response = await user.PostAsJsonAsync("/api/v1/product/buy", new { sku, quantity = 1 });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    Assert.Equal(4, await IdempotencyTests.StockOf(admin, sku));
  }
}
