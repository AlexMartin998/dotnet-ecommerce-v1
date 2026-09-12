using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Data;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// El catálogo como lo necesita una tienda: URL por slug, varias imágenes, etiquetas, y
/// retirar un producto sin romper las órdenes que lo compraron.
/// </summary>
[Collection(IntegrationCollection.Name)]
public class StorefrontCatalogTests(ApiFactory factory)
{
  // ---- slug ----------------------------------------------------------------

  [Fact]
  public async Task TheSlugIsDerivedFromTheNameAndServesTheProduct()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateAsync(admin, name: "Camión Ñandú 4x4");

    var byId = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}");
    var slug = byId.GetProperty("slug").GetString();

    // Sin acentos: "Camión" y "Camion" tienen que dar el mismo slug.
    Assert.Equal("camion-nandu-4x4", slug);

    // Anónimo: la ficha del producto es pública, igual que /product/{id}.
    using var anonymous = factory.Anonymous();
    var bySlug = await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/product/slug/{slug}");

    Assert.Equal(id, bySlug.GetProperty("id").GetInt32());
  }

  [Fact]
  public async Task TwoProductsWithTheSameNameConflictAndTheMessageSaysWhatToDo()
  {
    using var admin = await factory.AsAdminAsync();
    var name = $"Repetido {Guid.NewGuid():N}"[..18];

    await CreateAsync(admin, name: name);
    var second = await PostAsync(admin, name: name);

    Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);

    var problem = await second.Content.ReadFromJsonAsync<JsonElement>();
    // El cliente no eligió ese slug, así que el error tiene que decirle qué hacer.
    Assert.Contains("Send an explicit 'slug'", problem.GetProperty("detail").GetString());
  }

  [Fact]
  public async Task AnExplicitSlugWins()
  {
    using var admin = await factory.AsAdminAsync();
    var slug = $"mi-slug-{Guid.NewGuid():N}"[..20];

    var (id, _) = await CreateAsync(admin, name: "Nombre que no se usa", slug: slug);

    var product = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}");
    Assert.Equal(slug, product.GetProperty("slug").GetString());
  }

  [Fact]
  public async Task PatchingTheNameDoesNotMoveTheUrl()
  {
    // Cambiar el slug rompería los enlaces que ya circulan.
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateAsync(admin, name: $"Antes {Guid.NewGuid():N}"[..18]);

    var before = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}"))
        .GetProperty("slug").GetString();

    await admin.PatchAsJsonAsync($"/api/v1/product/{id}", new { name = "Otro nombre distinto" });

    var after = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}"))
        .GetProperty("slug").GetString();

    Assert.Equal(before, after);
  }

  // ---- etiquetas y tallas ---------------------------------------------------

  [Fact]
  public async Task TagsAndSizesTravelBothWays()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateAsync(admin, tags: ["ropa", "unisex"], sizes: ["M", "L"]);

    var product = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}");

    Assert.Equal(["ropa", "unisex"], product.GetProperty("tags").EnumerateArray().Select(t => t.GetString()));
    Assert.Equal(["M", "L"], product.GetProperty("sizes").EnumerateArray().Select(t => t.GetString()));
  }

  [Fact]
  public async Task PatchingTagsReplacesThemAndOmittingThemDoesNot()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateAsync(admin, tags: ["vieja"]);

    await admin.PatchAsJsonAsync($"/api/v1/product/{id}", new { tags = new[] { "nueva" } });
    Assert.Equal(["nueva"], await TagsOf(admin, id));

    // Omitirlas sigue significando "no tocar".
    await admin.PatchAsJsonAsync($"/api/v1/product/{id}", new { stock = 3 });
    Assert.Equal(["nueva"], await TagsOf(admin, id));
  }

  // ---- retirar un producto --------------------------------------------------

  [Fact]
  public async Task DeletingAProductHidesItButKeepsTheRow()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateAsync(admin);

    Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/product/{id}")).StatusCode);

    // Invisible para todo el mundo...
    Assert.Equal(HttpStatusCode.NotFound, (await admin.GetAsync($"/api/v1/product/{id}")).StatusCode);

    // ...pero la fila sigue ahí, que es de lo que dependen las órdenes.
    Assert.True(await ExistsInDbAsync(id));
  }

  [Fact]
  public async Task AProductThatWasSoldCanStillBeWithdrawn()
  {
    // Es la razón de ser del borrado lógico: con la FK, un borrado real fallaría.
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateAsync(admin, stock: 5);

    using var buyer = await factory.AsNewUserAsync();
    var order = await buyer.PostAsJsonAsync("/api/v1/order", new
    {
      items = new[] { new { sku, quantity = 1 } },
      customerName = "Cliente"
    });
    order.EnsureSuccessStatusCode();

    Assert.Equal(HttpStatusCode.NoContent, (await admin.DeleteAsync($"/api/v1/product/{id}")).StatusCode);

    // Y la orden sigue contando lo que se compró, con sus datos congelados.
    var placed = await order.Content.ReadFromJsonAsync<JsonElement>();
    var orderId = placed.GetProperty("id").GetInt32();

    var reread = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/order/{orderId}");
    Assert.Equal(sku, reread.GetProperty("items")[0].GetProperty("sku").GetString());
  }

  [Fact]
  public async Task DeletingTwiceIsA404TheSecondTime()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateAsync(admin);

    await admin.DeleteAsync($"/api/v1/product/{id}");

    Assert.Equal(HttpStatusCode.NotFound, (await admin.DeleteAsync($"/api/v1/product/{id}")).StatusCode);
  }

  [Fact]
  public async Task AWithdrawnProductReleasesItsSkuAndItsSlug()
  {
    // El índice único va filtrado por DeletedAt: si no, un producto retirado bloquearía
    // su SKU y su slug para siempre y no habría forma de reutilizarlos.
    using var admin = await factory.AsAdminAsync();
    var name = $"Reuso {Guid.NewGuid():N}"[..18];
    var (id, sku) = await CreateAsync(admin, name: name);

    await admin.DeleteAsync($"/api/v1/product/{id}");

    var again = await PostAsync(admin, name: name, sku: sku);
    Assert.Equal(HttpStatusCode.Created, again.StatusCode);
  }

  [Fact]
  public async Task AWithdrawnProductCannotBeBought()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateAsync(admin, stock: 5);

    await admin.DeleteAsync($"/api/v1/product/{id}");

    using var buyer = await factory.AsNewUserAsync();
    var response = await buyer.PostAsJsonAsync("/api/v1/order", new
    {
      items = new[] { new { sku, quantity = 1 } },
      customerName = "Cliente"
    });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  [Fact]
  public async Task AWithdrawnProductDisappearsFromTheQuoteToo()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateAsync(admin, stock: 5);

    await admin.DeleteAsync($"/api/v1/product/{id}");

    using var anonymous = factory.Anonymous();
    var quote = await (await anonymous.PostAsJsonAsync("/api/v1/cart/quote", new
    {
      items = new[] { new { sku, quantity = 1 } }
    })).Content.ReadFromJsonAsync<JsonElement>();

    Assert.Equal("not_found", quote.GetProperty("items")[0].GetProperty("status").GetString());
  }

  // ---- helpers -------------------------------------------------------------

  private static async Task<HttpResponseMessage> PostAsync(
      HttpClient admin, string? name = null, string? slug = null, string? sku = null,
      int stock = 10, string[]? tags = null, string[]? sizes = null)
  {
    var category = await admin.PostAsJsonAsync("/api/v1/category",
        new { name = VersioningAndHealthTests.Unique("Cat") });
    var categoryId = int.Parse(category.Headers.Location!.Segments[^1]);

    return await admin.PostAsJsonAsync("/api/v1/product", new
    {
      name = name ?? VersioningAndHealthTests.Unique("Prod"),
      slug,
      price = 9.99m,
      sku = sku ?? $"SKU-{Guid.NewGuid():N}"[..20],
      stock,
      categoryId,
      tags = tags ?? [],
      sizes = sizes ?? []
    });
  }

  /// <remarks>
  /// El id sale de la cabecera <c>Location</c>: <c>POST /product</c> devuelve 201 SIN cuerpo,
  /// igual que el de categoría.
  /// </remarks>
  private static async Task<(int Id, string Sku)> CreateAsync(
      HttpClient admin, string? name = null, string? slug = null, int stock = 10,
      string[]? tags = null, string[]? sizes = null)
  {
    var sku = $"SKU-{Guid.NewGuid():N}"[..20];

    var response = await PostAsync(admin, name, slug, sku, stock, tags, sizes);
    response.EnsureSuccessStatusCode();

    return (int.Parse(response.Headers.Location!.Segments[^1]), sku);
  }

  private static async Task<IEnumerable<string?>> TagsOf(HttpClient client, int id)
      => (await client.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}"))
          .GetProperty("tags").EnumerateArray().Select(t => t.GetString()).ToList();

  /// <summary>Mira la fila saltándose el filtro global, que es justo lo que se prueba.</summary>
  private async Task<bool> ExistsInDbAsync(int id)
  {
    using var scope = factory.Services.CreateScope();

    return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
        .Products.IgnoreQueryFilters()
        .AnyAsync(p => p.Id == id);
  }
}
