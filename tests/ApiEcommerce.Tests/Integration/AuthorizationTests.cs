using System.Net;
using System.Net.Http.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// La matriz de autorización de <c>AGENTS/features/02</c>, endpoint a endpoint.
/// </summary>
/// <remarks>
/// Es el bloque de tests más valioso del proyecto y el que <b>no se puede escribir con
/// mocks</b>: quien decide 401 frente a 403 es el pipeline de ASP.NET Core, no el
/// controller. Y protege la trampa que ya mordió una vez — varios <c>[Authorize]</c> se
/// <b>combinan (AND)</b>, no se sobreescriben, así que con
/// <c>[Authorize(Roles = "admin")]</c> en la clase la compra devolvía 403 a un cliente.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class AuthorizationTests(ApiFactory factory)
{
  // ---- el catálogo es público ---------------------------------------------

  [Theory]
  [InlineData("/api/v1/category")]
  [InlineData("/api/v1/category/paged")]
  [InlineData("/api/v1/product")]
  [InlineData("/api/v1/product/paged")]
  [InlineData("/api/v1/product/search?name=x")]
  public async Task ReadingTheCatalogNeedsNoToken(string endpoint)
  {
    using var client = factory.Anonymous();

    Assert.Equal(HttpStatusCode.OK, (await client.GetAsync(endpoint)).StatusCode);
  }

  // ---- las escrituras exigen admin ----------------------------------------

  [Theory]
  [InlineData("POST", "/api/v1/category")]
  [InlineData("PATCH", "/api/v1/category/1")]
  [InlineData("DELETE", "/api/v1/category/1")]
  [InlineData("POST", "/api/v1/product")]
  [InlineData("PATCH", "/api/v1/product/1")]
  [InlineData("DELETE", "/api/v1/product/1")]
  public async Task WritingWithAPlainUserTokenIs403(string method, string endpoint)
  {
    // 403 y no 401: sé quién eres y no puedes. Un 401 aquí haría que el cliente
    // reintentara pidiendo credenciales que no le van a servir de nada.
    using var user = await factory.AsNewUserAsync();

    var response = await user.SendAsync(Request(method, endpoint));

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  [Theory]
  [InlineData("POST", "/api/v1/category")]
  [InlineData("DELETE", "/api/v1/product/1")]
  [InlineData("POST", "/api/v1/product/buy")]
  public async Task WritingWithNoTokenIs401(string method, string endpoint)
  {
    // 401 y no 403: no sé quién eres.
    using var client = factory.Anonymous();

    var response = await client.SendAsync(Request(method, endpoint));

    Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
  }

  [Fact]
  public async Task AnAdminCanWrite()
  {
    using var admin = await factory.AsAdminAsync();

    var response = await admin.PostAsJsonAsync("/api/v1/category",
        new { name = VersioningAndHealthTests.Unique("Auth") });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
  }

  // ---- comprar: basta estar autenticado -----------------------------------

  [Fact]
  public async Task BuyingOnlyRequiresBeingAuthenticated()
  {
    // ⚠️ El escenario que da nombre a la trampa. Exigir admin para comprar —como hacía
    // el código del curso— no tiene ningún sentido en una tienda, y es exactamente lo
    // que pasa si el requisito fuerte se pone en la clase.
    using var admin = await factory.AsAdminAsync();
    var sku = await CreateProductAsync(admin, stock: 5);

    using var user = await factory.AsNewUserAsync();
    var response = await user.PostAsJsonAsync("/api/v1/product/buy", new { sku, quantity = 1 });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  [Fact]
  public async Task AnExpiredOrGarbledTokenIs401AndNot500()
  {
    using var client = factory.Anonymous();
    client.DefaultRequestHeaders.Authorization = new("Bearer", "esto.no.es.un.jwt");

    Assert.Equal(HttpStatusCode.Unauthorized, (await client.GetAsync("/api/v1/auth/me")).StatusCode);
  }

  // ---- helpers ------------------------------------------------------------

  private static HttpRequestMessage Request(string method, string endpoint)
      => new(new HttpMethod(method), endpoint)
      {
        // Cuerpo vacío pero válido: si faltara, un 415/400 taparía el 401/403 que se prueba.
        Content = JsonContent.Create(new { })
      };

  internal static async Task<string> CreateProductAsync(HttpClient admin, int stock)
  {
    var categoryResponse = await admin.PostAsJsonAsync("/api/v1/category",
        new { name = VersioningAndHealthTests.Unique("Cat") });
    var categoryId = int.Parse(categoryResponse.Headers.Location!.Segments[^1]);

    var sku = $"SKU-{Guid.NewGuid():N}"[..20];

    var productResponse = await admin.PostAsJsonAsync("/api/v1/product",
        new { name = "Producto de prueba", price = 9.99m, sku, stock, categoryId });

    productResponse.EnsureSuccessStatusCode();

    return sku;
  }
}
