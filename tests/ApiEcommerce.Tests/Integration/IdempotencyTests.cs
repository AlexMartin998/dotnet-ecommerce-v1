using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Shared.Idempotency;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Idempotencia con <c>Idempotency-Key</c>. Se prueba contra Redis <b>real</b>: es un
/// filtro cuyo valor entero está en la reserva atómica (<c>SET NX</c>), y con una
/// implementación falsa en memoria no se estaría probando nada.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class IdempotencyTests(ApiFactory factory)
{
  [Fact]
  public async Task RetryingWithTheSameKeyReplaysTheResponseAndDoesNotBuyTwice()
  {
    // El caso real: el móvil pierde la conexión tras enviar la compra y el usuario
    // vuelve a pulsar. Sin esto se le cobra dos veces.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var first = await Buy(user, sku, 3, key);
    var second = await Buy(user, sku, 3, key);

    Assert.Equal(HttpStatusCode.OK, first.StatusCode);
    Assert.Equal(HttpStatusCode.OK, second.StatusCode);

    // Lo que de verdad importa no es el código, es el STOCK: 10 - 3, no 10 - 6.
    Assert.Equal(7, await StockOf(admin, sku));
  }

  [Fact]
  public async Task TheReplayReturnsTheSameBody()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var first = await (await Buy(user, sku, 1, key)).Content.ReadAsStringAsync();
    var second = await (await Buy(user, sku, 1, key)).Content.ReadAsStringAsync();

    // Reproducir la respuesta ORIGINAL, no ejecutar otra vez y devolver una parecida.
    //
    // ⭐ Se comparan los BYTES, no el JSON parseado. Durante mucho tiempo esto no se pudo
    // exigir: el filtro memorizaba el cuerpo ya serializado y lo devolvía tal cual, y
    // `System.Text.Json` escapa `+` como `\u002B` mientras que la respuesta viva de MVC
    // lo emite crudo — dos cuerpos equivalentes que no eran idénticos, y sólo cuando el
    // base64 del `rowVersion` llevaba un `+`, o sea de forma intermitente.
    //
    // Al bajar la garantía a la transacción, lo que se memoriza es el DTO y no la
    // respuesta HTTP, así que el replay vuelve a pasar por el MISMO formateador de MVC.
    // La deuda se cerró como efecto secundario de arreglar dónde vivía la marca.
    Assert.Equal(first, second);
  }

  [Fact]
  public async Task WithoutTheHeaderTheOperationRunsEveryTime()
  {
    // La idempotencia es OPCIONAL y la pide el cliente, que es quien sabe si está
    // reintentando. Sin cabecera, dos compras son dos compras.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    await Buy(user, sku, 2, key: null);
    await Buy(user, sku, 2, key: null);

    Assert.Equal(6, await StockOf(admin, sku));
  }

  [Fact]
  public async Task ADifferentKeyIsADifferentOperation()
  {
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    await Buy(user, sku, 2, Guid.NewGuid().ToString());
    await Buy(user, sku, 2, Guid.NewGuid().ToString());

    Assert.Equal(6, await StockOf(admin, sku));
  }

  [Fact]
  public async Task AFailedOperationReleasesTheKeySoTheClientCanRetry()
  {
    // ⚠️ Solo se memoriza el ÉXITO. Memorizar un 409 convertiría un fallo transitorio
    // —comprar más de lo que hay, y que luego entre stock— en un fallo PERMANENTE
    // durante 24 h para esa clave, sin forma de que el cliente salga del bucle.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 1);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var failed = await Buy(user, sku, 5, key);
    Assert.Equal(HttpStatusCode.Conflict, failed.StatusCode);

    // Misma clave, ahora con una cantidad que sí cabe: debe ejecutarse, no reproducir el 409.
    var retried = await Buy(user, sku, 1, key);

    Assert.Equal(HttpStatusCode.OK, retried.StatusCode);
    Assert.Equal(0, await StockOf(admin, sku));
  }

  [Fact]
  public async Task ReusingTheKeyWithADifferentBodyIs422AndDoesNotExecute()
  {
    // ⚠️ Antes reproducía la respuesta de la primera EN SILENCIO: el cliente pedía 5
    // unidades, recibía un 200 con el resultado de haber comprado 1, y nada indicaba que
    // su petición no se había ejecutado. Un fallo mudo en el mecanismo que existe para no
    // cobrar de más.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    Assert.Equal(HttpStatusCode.OK, (await Buy(user, sku, 1, key)).StatusCode);

    var reused = await Buy(user, sku, 5, key);

    Assert.Equal(HttpStatusCode.UnprocessableEntity, reused.StatusCode);
    // Y sobre todo: no se ejecutó. 10 - 1, no 10 - 6.
    Assert.Equal(9, await StockOf(admin, sku));
  }

  [Fact]
  public async Task TheKeyIsScopedToTheUser()
  {
    // ⚠️ Sin el usuario en la clave, dos clientes que generen el mismo GUID se pisan —y
    // peor, uno recibe la respuesta del otro, que es una fuga de datos entre cuentas.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    var key = Guid.NewGuid().ToString();

    using var first = await factory.AsNewUserAsync();
    using var second = await factory.AsNewUserAsync();

    await Buy(first, sku, 2, key);
    await Buy(second, sku, 2, key);   // MISMA clave, otro usuario: NO se reproduce

    Assert.Equal(6, await StockOf(admin, sku));
  }

  [Fact]
  public async Task TheReplayIsMarkedWithTheReplayedHeader()
  {
    // La cabecera es parte del contrato y estaba en el .feature, pero no la afirmaba
    // ningún test: se podía haber dejado de emitir sin que nada se pusiera rojo. Es lo
    // único que distingue "tu compra se ejecutó ahora" de "te devuelvo la de antes".
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var first = await Buy(user, sku, 2, key);
    var second = await Buy(user, sku, 2, key);

    Assert.False(first.Headers.Contains(IdempotentAttribute.ReplayedHeader));
    Assert.Equal("true", second.Headers.GetValues(IdempotentAttribute.ReplayedHeader).Single());
  }

  [Fact]
  public async Task AKeyLongerThanTheLimitIsRejectedAndNothingRuns()
  {
    // La clave la elige el cliente y acaba entera dentro de una clave de Redis que vive
    // 24 h, en una instancia compartida con otros proyectos. Sin límite se aceptaban
    // claves de 7000 caracteres (medido contra la API corriendo).
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var user = await factory.AsNewUserAsync();

    var response = await Buy(user, sku, 3, new string('k', 256));

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);

    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal("idempotency_key_invalid", problem.GetProperty("code").GetString());

    // Y sobre todo: se rechaza ANTES de ejecutar nada.
    Assert.Equal(10, await StockOf(admin, sku));
  }

  [Fact]
  public async Task EachUserGetsItsOwnResponseAndNotTheOtherOnes()
  {
    // El test que ya había miraba el stock, que sube a 6 tanto si cada uno ejecutó lo
    // suyo como si algo raro pasó por medio. Lo que de verdad hay que descartar es la
    // FUGA ENTRE CUENTAS: que B reciba el cuerpo de la compra de A. Eso sólo se ve
    // comparando los cuerpos.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 10);

    using var a = await factory.AsNewUserAsync();
    using var b = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var first = await Buy(a, sku, 2, key);
    var second = await Buy(b, sku, 3, key);   // MISMA clave, otro usuario

    var firstBody = await first.Content.ReadFromJsonAsync<JsonElement>();
    var secondBody = await second.Content.ReadFromJsonAsync<JsonElement>();

    // Cada uno ve el stock que dejó SU compra, no el del otro.
    Assert.Equal(8, firstBody.GetProperty("stock").GetInt32());
    Assert.Equal(5, secondBody.GetProperty("stock").GetInt32());
    Assert.False(second.Headers.Contains(IdempotentAttribute.ReplayedHeader));
  }

  [Fact]
  public async Task ConcurrentRequestsWithTheSameKeyBuyOnceAndTheLosersSayWhy()
  {
    // Simultáneas, NUNCA en secuencia: en secuencia esto pasaba también con la
    // implementación que tenía la carrera.
    //
    // Y se afirma "exactamente UNA no reproducida" en vez de "exactamente un 200": los
    // que llegan después de que la primera termine reciben 200 CON la cabecera de
    // replay, que es correcto. Lo que no puede haber es dos ejecuciones de verdad.
    using var admin = await factory.AsAdminAsync();
    var sku = await AuthorizationTests.CreateProductAsync(admin, stock: 20);

    using var user = await factory.AsNewUserAsync();
    var key = Guid.NewGuid().ToString();

    var responses = await Task.WhenAll(
        Enumerable.Range(0, 8).Select(_ => Buy(user, sku, 2, key)));

    var executed = responses.Count(r =>
        r.StatusCode == HttpStatusCode.OK && !r.Headers.Contains(IdempotentAttribute.ReplayedHeader));

    Assert.Equal(1, executed);
    Assert.Equal(18, await StockOf(admin, sku));

    // Quien pierde la carrera tiene que poder distinguir este 409 del 409 de "no hay
    // stock": son dos cosas muy distintas para un cliente que reintenta.
    foreach (var conflict in responses.Where(r => r.StatusCode == HttpStatusCode.Conflict))
    {
      var problem = await conflict.Content.ReadFromJsonAsync<JsonElement>();
      Assert.Equal("idempotency_in_progress", problem.GetProperty("code").GetString());
    }

    foreach (var response in responses) response.Dispose();
  }

  // ---- helpers ------------------------------------------------------------

  private static Task<HttpResponseMessage> Buy(HttpClient client, string sku, int quantity, string? key)
  {
    var request = new HttpRequestMessage(HttpMethod.Post, "/api/v1/product/buy")
    {
      Content = JsonContent.Create(new { sku, quantity })
    };

    if (key is not null) request.Headers.Add(IdempotentAttribute.HeaderName, key);

    return client.SendAsync(request);
  }

  internal static async Task<int> StockOf(HttpClient client, string sku)
  {
    var response = await client.GetAsync($"/api/v1/product/paged?page=1&pageSize=100");
    var page = await response.Content.ReadFromJsonAsync<JsonElement>();

    return page.GetProperty("items").EnumerateArray()
        .Single(i => i.GetProperty("sku").GetString() == sku)
        .GetProperty("stock").GetInt32();
  }
}
