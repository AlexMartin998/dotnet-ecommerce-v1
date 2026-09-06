using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary><c>ETag</c> / <c>If-Match</c>: el <i>lost update</i> entre dos administradores.</summary>
/// <remarks>
/// <c>Product.RowVersion</c> por sí solo no cierra esta carrera: el PATCH de A relee la
/// fila y EF compara contra el rowversion de B, así que cuadra y A pisa el cambio. Solo
/// rompe el empate el token que A leyó en su GET, y ese llega del cliente.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class ConcurrencyControlTests(ApiFactory factory)
{
  [Fact]
  public async Task TheGetPublishesAnETag()
  {
    using var admin = await factory.AsAdminAsync();
    var id = await CreateProductAsync(admin);

    var response = await admin.GetAsync($"/api/v1/product/{id}");

    Assert.NotNull(response.Headers.ETag);
    Assert.False(response.Headers.ETag!.IsWeak);
  }

  [Fact]
  public async Task AStaleIfMatchIs412AndDoesNotOverwriteTheOtherChange()
  {
    using var admin = await factory.AsAdminAsync();
    var id = await CreateProductAsync(admin);

    // A lee y se guarda el ETag.
    var etag = (await admin.GetAsync($"/api/v1/product/{id}")).Headers.ETag!.Tag.Trim('"');

    // B edita (sin If-Match: sigue siendo válido, la protección es opcional).
    Assert.Equal(HttpStatusCode.NoContent,
        (await admin.PatchAsJsonAsync($"/api/v1/product/{id}", new { price = 99.99m })).StatusCode);

    // A guarda con su token viejo.
    var stale = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/product/{id}")
    {
      Content = JsonContent.Create(new { price = 1.00m })
    };
    stale.Headers.Add("If-Match", $"\"{etag}\"");

    Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.SendAsync(stale)).StatusCode);

    // Lo que de verdad importa: el cambio de B sigue ahí.
    Assert.Equal(99.99m, await PriceOf(admin, id));
  }

  [Fact]
  public async Task AFreshIfMatchSucceeds()
  {
    using var admin = await factory.AsAdminAsync();
    var id = await CreateProductAsync(admin);

    var etag = (await admin.GetAsync($"/api/v1/product/{id}")).Headers.ETag!.Tag.Trim('"');

    var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/product/{id}")
    {
      Content = JsonContent.Create(new { price = 7.77m })
    };
    request.Headers.Add("If-Match", $"\"{etag}\"");

    Assert.Equal(HttpStatusCode.NoContent, (await admin.SendAsync(request)).StatusCode);
    Assert.Equal(7.77m, await PriceOf(admin, id));
  }

  [Fact]
  public async Task WithoutIfMatchThePatchKeepsWorking()
  {
    // La protección es opcional: exigirla rompería a todos los clientes actuales.
    using var admin = await factory.AsAdminAsync();
    var id = await CreateProductAsync(admin);

    Assert.Equal(HttpStatusCode.NoContent,
        (await admin.PatchAsJsonAsync($"/api/v1/product/{id}", new { price = 3.33m })).StatusCode);
  }

  [Fact]
  public async Task AMalformedIfMatchIs400AndNot500()
  {
    // No es que la precondición falle: no es ni un token, y sin tratarlo el
    // Convert.FromBase64String lanza FormatException y sale un 500.
    using var admin = await factory.AsAdminAsync();
    var id = await CreateProductAsync(admin);

    var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/product/{id}")
    {
      Content = JsonContent.Create(new { price = 2.00m })
    };
    request.Headers.TryAddWithoutValidation("If-Match", "\"esto-no-es-base64!!\"");

    Assert.Equal(HttpStatusCode.BadRequest, (await admin.SendAsync(request)).StatusCode);
  }

  private static async Task<int> CreateProductAsync(HttpClient admin)
  {
    var category = await admin.PostAsJsonAsync("/api/v1/category",
        new { name = VersioningAndHealthTests.Unique("Etag") });
    var categoryId = int.Parse(category.Headers.Location!.Segments[^1]);

    var product = await admin.PostAsJsonAsync("/api/v1/product", new
    {
      name = "Producto con version",
      price = 9.99m,
      sku = $"ETG-{Guid.NewGuid():N}"[..20],
      stock = 5,
      categoryId
    });

    product.EnsureSuccessStatusCode();

    return int.Parse(product.Headers.Location!.Segments[^1]);
  }

  private static async Task<decimal> PriceOf(HttpClient client, int id)
      => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}"))
          .GetProperty("price").GetDecimal();
}
