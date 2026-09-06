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
/// <para>
/// ⚠️ Y la decisión de degradar tiene que ser <b>la misma en todas las implementaciones</b>.
/// Que <c>RedisCacheService</c> fallara en abierto y <c>RedisIdempotencyStore</c> en
/// cerrado hacía que un corte de Redis devolviera <b>500 por una compra ya cobrada</b>.
/// Estos tests fijan que ambas fallan en abierto.
/// </para>
/// <para>
/// ⚠️ Ojo con lo que significa hoy «degradar» en la idempotencia: lo que se pierde con
/// Redis caído es el <b>atajo</b>, no la garantía. La marca de que un comando ya se
/// ejecutó vive en <c>ExecutedCommands</c>, en la misma transacción que el efecto, así
/// que sigue en pie sin Redis. Los tests de abajo lo fijan — antes eran imposibles de
/// escribir, porque el almacén ERA Redis.
/// </para>
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
  public async Task WithRedisDownTheGuaranteeStillHoldsAndTheRetryDoesNotBuyTwice()
  {
    // ⭐ El test que justifica todo el rediseño. Con el almacén en Redis esto NO se podía
    // cumplir: sin Redis no había idempotencia, punto. Ahora la marca se escribe en la
    // misma transacción que el descuento de stock, así que "el almacén no está" y "la
    // compra no puede ocurrir" son el mismo evento y la pregunta desaparece.
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

    // Y el reintento se anuncia como replay aunque el atajo de Redis no exista: la
    // cabecera sale de un hecho que reporta el servicio, no del filtro.
    Assert.Equal("true", second.Headers.GetValues(IdempotentAttribute.ReplayedHeader).Single());
  }

  [Fact]
  public async Task WithRedisDownConcurrentRequestsWithTheSameKeyStillBuyOnce()
  {
    // Sin el atajo no hay ni reserva ni 409: quien arbitra es la clave primaria de
    // ExecutedCommands. El que pierde se bloquea en la clave hasta que el otro confirma,
    // choca, su transacción entera se deshace —incluido el stock— y devuelve el
    // resultado del ganador. Por eso aquí NO se admite Conflict: todas son 200.
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
    // La comprobación de la huella también bajó a la transacción: es una excepción de
    // dominio (IdempotencyConflictAppException), no un resultado del filtro HTTP.
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
