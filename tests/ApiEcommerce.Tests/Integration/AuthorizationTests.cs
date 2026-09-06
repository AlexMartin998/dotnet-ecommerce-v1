using System.Net;
using System.Net.Http.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary>La matriz de autorización, endpoint a endpoint.</summary>
/// <remarks>
/// No se puede escribir con mocks: quien decide 401 frente a 403 es el pipeline, no el
/// controller. Y varios <c>[Authorize]</c> se combinan con AND, así que el requisito
/// fuerte nunca va en la clase.
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
    // 403 y no 401: un 401 haría al cliente reintentar con credenciales que no sirven.
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
    // Exigir admin para comprar no tiene sentido en una tienda, y es exactamente lo que
    // pasa si el requisito fuerte se pone a nivel de clase.
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
