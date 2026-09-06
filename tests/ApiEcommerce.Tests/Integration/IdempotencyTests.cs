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

    var first = await (await Buy(user, sku, 1, key)).Content.ReadFromJsonAsync<JsonElement>();
    var second = await (await Buy(user, sku, 1, key)).Content.ReadFromJsonAsync<JsonElement>();

    // Reproducir la respuesta ORIGINAL, no ejecutar otra vez y devolver una parecida:
    // el cliente debe ver el mismo recurso, con el mismo stock y la misma versión.
    //
    // ⚠️ Se compara el JSON PARSEADO y no los bytes. El replay re-serializa el cuerpo
    // memorizado, y `System.Text.Json` escapa `+` como `\u002B` mientras que la respuesta
    // viva de MVC lo emite crudo: dos cuerpos equivalentes que no son idénticos byte a
    // byte. Solo se nota cuando el base64 del `rowVersion` contiene un `+`, o sea de
    // forma intermitente y dependiente de los datos — que es como se descubrió.
    // Byte-identidad exigiría capturar lo que MVC escribe de verdad; está anotado como
    // deuda en planning/12 y no afecta a ningún cliente que parsee JSON.
    Assert.Equal(first.GetProperty("id").GetInt32(), second.GetProperty("id").GetInt32());
    Assert.Equal(first.GetProperty("stock").GetInt32(), second.GetProperty("stock").GetInt32());
    Assert.Equal(first.GetProperty("rowVersion").GetString(), second.GetProperty("rowVersion").GetString());
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
