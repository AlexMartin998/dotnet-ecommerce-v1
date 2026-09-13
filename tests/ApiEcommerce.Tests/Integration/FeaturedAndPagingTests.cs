using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Destacadas del header y listados paginados para infinite scroll (<c>planning/28</c>,
/// <c>features/28_destacadas-y-paginacion.feature</c>).
/// </summary>
/// <remarks>
/// Las destacadas son estado global de la base: cada test fija las suyas antes de mirar.
/// </remarks>
[Collection(IntegrationCollection.Name)]
public class FeaturedAndPagingTests(ApiFactory factory)
{
  // ---- A. destacadas -----------------------------------------------------------

  [Fact]
  public async Task FeaturedCategoriesAreReplacedInOrderAndReadAnonymously()
  {
    using var admin = await factory.AsAdminAsync();
    var a = await CreateCategoryAsync(admin);
    var b = await CreateCategoryAsync(admin);
    var c = await CreateCategoryAsync(admin);

    await SetFeaturedAsync(admin, a, b, c);
    // Reordenar y quitar una: [a,b,c] -> [c,a]. Reordenar es lo que chocaría con el índice
    // único si se escribiera posición a posición sin limpiar antes.
    var response = await admin.PutAsJsonAsync("/api/v1/category/featured", new { categoryIds = new[] { c, a } });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);

    using var anonymous = factory.Anonymous();
    var featured = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/category/featured");

    Assert.Equal([c, a], featured.EnumerateArray().Select(x => x.GetProperty("id").GetInt32()));
    Assert.Equal([1, 2], featured.EnumerateArray().Select(x => x.GetProperty("featuredPosition").GetInt32()));

    // La que salió ya no lo es, también en su lectura por id (que va cacheada).
    var old = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/category/{b}");
    Assert.Equal(JsonValueKind.Null, old.GetProperty("featuredPosition").ValueKind);
  }

  [Fact]
  public async Task AFourthFeaturedCategoryHasAStableCodeAndChangesNothing()
  {
    using var admin = await factory.AsAdminAsync();
    var ids = new[] { await CreateCategoryAsync(admin), await CreateCategoryAsync(admin), await CreateCategoryAsync(admin), await CreateCategoryAsync(admin) };
    await SetFeaturedAsync(admin, ids[0]);

    var response = await admin.PutAsJsonAsync("/api/v1/category/featured", new { categoryIds = ids });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
    Assert.Equal("featured_limit_reached", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
    Assert.Equal([ids[0]], await FeaturedIdsAsync(admin));
  }

  [Fact]
  public async Task RepeatedOrUnknownIdsAreRejected()
  {
    using var admin = await factory.AsAdminAsync();
    var a = await CreateCategoryAsync(admin);

    Assert.Equal(HttpStatusCode.BadRequest,
        (await admin.PutAsJsonAsync("/api/v1/category/featured", new { categoryIds = new[] { a, a } })).StatusCode);
    Assert.Equal(HttpStatusCode.BadRequest,
        (await admin.PutAsJsonAsync("/api/v1/category/featured", new { categoryIds = new[] { a, int.MaxValue } })).StatusCode);
  }

  [Fact]
  public async Task AnEmptyListUnfeaturesThemAll()
  {
    using var admin = await factory.AsAdminAsync();
    await SetFeaturedAsync(admin, await CreateCategoryAsync(admin));

    await SetFeaturedAsync(admin);

    Assert.Empty(await FeaturedIdsAsync(admin));
  }

  [Fact]
  public async Task TheDatabaseRejectsAFourthPositionEvenBypassingTheService()
  {
    // El límite no depende del servicio: el CHECK de la base lo impide.
    using var admin = await factory.AsAdminAsync();
    var id = await CreateCategoryAsync(admin);

    using var scope = factory.Services.CreateScope();
    var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();

    var ex = await Assert.ThrowsAnyAsync<Exception>(() => db.Categories
        .Where(c => c.Id == id)
        .ExecuteUpdateAsync(s => s.SetProperty(c => c.FeaturedPosition, (int?)4)));

    Assert.Contains("CK_Categories_FeaturedPosition", ex.ToString());
  }

  [Fact]
  public async Task ConcurrentReplacementsNeverLeaveMoreThanThreeNorFail()
  {
    using var admin = await factory.AsAdminAsync();
    var ids = new List<int>();
    for (var i = 0; i < 6; i++) ids.Add(await CreateCategoryAsync(admin));

    // Simultáneos y con listas que se pisan: sin el applock se intercalan las sentencias.
    var sets = Enumerable.Range(0, 10).Select(i => ids.Skip(i % 4).Take(3).Reverse().ToArray()).ToList();
    var responses = await Task.WhenAll(sets.Select(set =>
        admin.PutAsJsonAsync("/api/v1/category/featured", new { categoryIds = set })));

    Assert.All(responses, r => Assert.Equal(HttpStatusCode.OK, r.StatusCode));

    var featured = await FeaturedIdsAsync(admin);
    Assert.Equal(3, featured.Count);
    Assert.Contains(featured.ToArray(), sets.Select(set => set));
  }

  [Fact]
  public async Task OnlyAnAdminSetsFeaturedCategories()
  {
    using var user = await factory.AsNewUserAsync();

    var response = await user.PutAsJsonAsync("/api/v1/category/featured", new { categoryIds = Array.Empty<int>() });

    Assert.Equal(HttpStatusCode.Forbidden, response.StatusCode);
  }

  // ---- B. paginación -------------------------------------------------------------

  [Fact]
  public async Task ACategoryIsPagedWithoutOverlapAndWithItsTotal()
  {
    using var admin = await factory.AsAdminAsync();
    var categoryId = await CreateCategoryAsync(admin);
    var slug = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/category/{categoryId}")).GetProperty("slug").GetString();
    for (var i = 0; i < 3; i++) await CreateProductAsync(admin, categoryId, VersioningAndHealthTests.Unique("Paged"));

    using var anonymous = factory.Anonymous();
    var first = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/product/category/slug/{slug}/paged?page=1&pageSize=2");
    var second = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/product/category/slug/{slug}/paged?page=2&pageSize=2");

    var ids = Ids(first).Concat(Ids(second)).ToList();
    Assert.Equal(3, first.GetProperty("totalItems").GetInt32());
    Assert.Equal(2, Ids(first).Count);
    Assert.Single(Ids(second));
    Assert.Equal(3, ids.Distinct().Count());

    // Orden total: createdAt desc, id desc.
    Assert.Equal(ids.OrderByDescending(id => id), ids);
  }

  [Fact]
  public async Task AnUnknownCategorySlugIsNotFound()
  {
    using var anonymous = factory.Anonymous();

    var response = await anonymous.GetAsync("/api/v1/product/category/slug/no-existe-nunca/paged");

    Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
  }

  [Fact]
  public async Task TheSearchIsPagedAndAnEmptyNameIsAnEmptyPage()
  {
    using var admin = await factory.AsAdminAsync();
    var categoryId = await CreateCategoryAsync(admin);
    var marker = $"Zq{Guid.NewGuid():N}"[..12];
    for (var i = 0; i < 3; i++) await CreateProductAsync(admin, categoryId, $"{marker} {i}");

    using var anonymous = factory.Anonymous();
    var page = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/product/search/paged?name={marker}&page=2&pageSize=2");

    Assert.Equal(3, page.GetProperty("totalItems").GetInt32());
    Assert.Single(Ids(page));

    var empty = await anonymous.GetFromJsonAsync<JsonElement>("/api/v1/product/search/paged");
    Assert.Equal(0, empty.GetProperty("totalItems").GetInt32());
    Assert.Empty(Ids(empty));
  }

  [Fact]
  public async Task AnOversizedPageIsAValidationError()
  {
    using var anonymous = factory.Anonymous();

    var response = await anonymous.GetAsync("/api/v1/product/search/paged?name=a&pageSize=1000");

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  // ---- helpers -------------------------------------------------------------------

  private static List<int> Ids(JsonElement page)
      => [.. page.GetProperty("items").EnumerateArray().Select(p => p.GetProperty("id").GetInt32())];

  private static async Task<int> CreateCategoryAsync(HttpClient admin)
  {
    var response = await admin.PostAsJsonAsync("/api/v1/category", new { name = VersioningAndHealthTests.Unique("Feat") });
    response.EnsureSuccessStatusCode();
    return int.Parse(response.Headers.Location!.Segments[^1]);
  }

  private static async Task CreateProductAsync(HttpClient admin, int categoryId, string name)
  {
    var response = await admin.PostAsJsonAsync("/api/v1/product", new
    {
      name,
      price = 5m,
      sku = $"P-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
      stock = 1,
      categoryId
    });
    response.EnsureSuccessStatusCode();
  }

  private static async Task SetFeaturedAsync(HttpClient admin, params int[] ids)
  {
    var response = await admin.PutAsJsonAsync("/api/v1/category/featured", new { categoryIds = ids });
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  private static async Task<List<int>> FeaturedIdsAsync(HttpClient client)
      => [.. (await client.GetFromJsonAsync<JsonElement>("/api/v1/category/featured"))
          .EnumerateArray().Select(c => c.GetProperty("id").GetInt32())];
}
