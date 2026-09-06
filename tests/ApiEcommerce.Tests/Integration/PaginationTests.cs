using System.Net;
using System.Net.Http.Json;
using System.Text.Json;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Paginación de extremo a extremo: aquí se prueba lo que el unitario de
/// <c>PagedResult</c> no puede — el binding de la query string, el tope de
/// <c>pageSize</c> y qué código HTTP sale de una página vacía.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class PaginationTests(ApiFactory factory)
{
  [Fact]
  public async Task ThePagedListingCarriesItemsAndMetadata()
  {
    using var admin = await factory.AsAdminAsync();
    await admin.PostAsJsonAsync("/api/v1/category", new { name = VersioningAndHealthTests.Unique("Page") });

    var page = await Get(factory.Anonymous(), "/api/v1/category/paged?page=1&pageSize=2");

    Assert.Equal(1, page.GetProperty("page").GetInt32());
    Assert.Equal(2, page.GetProperty("pageSize").GetInt32());
    Assert.True(page.GetProperty("totalItems").GetInt32() >= 1);
    Assert.True(page.GetProperty("totalPages").GetInt32() >= 1);
  }

  [Fact]
  public async Task APageOutOfRangeIs200WithAnEmptyList()
  {
    // ⚠️ NO 404. El recurso —la colección— existe; lo que no hay son resultados en esa
    // página. El código del curso devolvía 404 con la tabla vacía, que es semánticamente
    // falso y obliga al cliente a tratar "no hay nada" como un error.
    using var client = factory.Anonymous();
    var response = await client.GetAsync("/api/v1/category/paged?page=9999&pageSize=10");

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    var page = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Empty(page.GetProperty("items").EnumerateArray());
    // Y el total real sigue viajando, para que el cliente sepa a dónde volver.
    Assert.True(page.GetProperty("totalItems").GetInt32() > 0);
  }

  [Theory]
  [InlineData("?page=0&pageSize=10")]
  [InlineData("?page=1&pageSize=0")]
  [InlineData("?page=-1&pageSize=10")]
  [InlineData("?page=1&pageSize=101")]
  public async Task PaginationParametersAreBounded(string query)
  {
    // ⚠️ El tope de pageSize no es cosmético: sin él, un ?pageSize=1000000 contra un
    // endpoint ANÓNIMO es una denegación de servicio de una sola petición.
    using var client = factory.Anonymous();

    Assert.Equal(HttpStatusCode.BadRequest,
        (await client.GetAsync($"/api/v1/category/paged{query}")).StatusCode);
  }

  [Fact]
  public async Task TheOrderIsStableAcrossPages()
  {
    // Un Skip/Take sobre una consulta sin orden estable puede devolver la misma fila en
    // dos páginas y saltarse otra. No se ve con pocos datos y es un infierno de depurar.
    using var admin = await factory.AsAdminAsync();
    for (var i = 0; i < 3; i++)
      await admin.PostAsJsonAsync("/api/v1/category", new { name = VersioningAndHealthTests.Unique("Order") });

    using var client = factory.Anonymous();
    var first = await Ids(client, "/api/v1/category/paged?page=1&pageSize=2");
    var second = await Ids(client, "/api/v1/category/paged?page=2&pageSize=2");

    Assert.Empty(first.Intersect(second));
  }

  [Fact]
  public async Task ThePagedProductListingBringsTheCategoryName()
  {
    // Sin el .Include(p => p.Category), CategoryName sale null EN SILENCIO: no falla
    // nada, simplemente el cliente recibe el campo vacío. Es el motivo de que
    // ProductService no delegue estas dos lecturas en el CRUD genérico.
    using var admin = await factory.AsAdminAsync();
    await AuthorizationTests.CreateProductAsync(admin, stock: 3);

    var page = await Get(factory.Anonymous(), "/api/v1/product/paged?page=1&pageSize=50");

    Assert.Contains(page.GetProperty("items").EnumerateArray(),
        item => !string.IsNullOrEmpty(item.GetProperty("categoryName").GetString()));
  }

  private static async Task<JsonElement> Get(HttpClient client, string url)
  {
    using (client)
    {
      var response = await client.GetAsync(url);
      response.EnsureSuccessStatusCode();
      return await response.Content.ReadFromJsonAsync<JsonElement>();
    }
  }

  private static async Task<List<int>> Ids(HttpClient client, string url)
  {
    var response = await client.GetAsync(url);
    var page = await response.Content.ReadFromJsonAsync<JsonElement>();

    return [.. page.GetProperty("items").EnumerateArray().Select(i => i.GetProperty("id").GetInt32())];
  }
}
