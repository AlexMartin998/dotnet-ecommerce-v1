using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Features.Ordering.Service;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;

namespace ApiEcommerce.Tests.Integration;


/// <summary>
/// Tallas como variantes con stock propio (<c>planning/27</c>,
/// <c>features/27_tallas-como-variantes.feature</c>).
/// </summary>
[Collection(IntegrationCollection.Name)]
public class ProductVariantTests(ApiFactory factory)
{
  // ---- ficha pública ---------------------------------------------------------

  [Fact]
  public async Task TheProductExposesItsActiveSizesWithStockAndAvailability()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateWithSizesAsync(admin, ("S", 3), ("M", 0), ("L", 2));

    var product = await PublicProductAsync(id);
    var variants = product.GetProperty("variants").EnumerateArray().ToList();

    Assert.Equal(["S", "M", "L"], variants.Select(v => v.GetProperty("size").GetString()));
    Assert.Equal([$"{sku}-S", $"{sku}-M", $"{sku}-L"], variants.Select(v => v.GetProperty("sku").GetString()));
    Assert.Equal([true, false, true], variants.Select(v => v.GetProperty("available").GetBoolean()));

    // Derivados de las variantes activas.
    Assert.Equal(5, product.GetProperty("stock").GetInt32());
    Assert.Equal(["S", "M", "L"], product.GetProperty("sizes").EnumerateArray().Select(s => s.GetString()));
  }

  [Fact]
  public async Task AProductWithoutSizesHasASingleVariantWithItsOwnSku()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateWithoutSizesAsync(admin, stock: 5);

    var variant = Assert.Single((await PublicProductAsync(id)).GetProperty("variants").EnumerateArray());

    Assert.Equal(JsonValueKind.Null, variant.GetProperty("size").ValueKind);
    Assert.Equal(sku, variant.GetProperty("sku").GetString());
    Assert.Equal(5, variant.GetProperty("stock").GetInt32());
  }

  [Fact]
  public async Task StockAndVariantsCannotBeSentTogether()
  {
    using var admin = await factory.AsAdminAsync();

    var response = await PostProductAsync(admin, stock: 5, variants: [new { size = "M", stock = 1 }]);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task ADeactivatedSizeDisappearsFromTheProductAndItsStock()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateWithSizesAsync(admin, ("S", 3), ("M", 4));

    await PatchVariantAsync(admin, id, await VariantIdAsync(admin, id, "S"), new { isActive = false });

    var product = await PublicProductAsync(id);
    Assert.Equal(["M"], product.GetProperty("sizes").EnumerateArray().Select(s => s.GetString()));
    Assert.Equal(4, product.GetProperty("stock").GetInt32());

    // El panel sí la ve.
    var all = await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}/variants");
    Assert.Equal(2, all.GetArrayLength());
  }

  // ---- cotización ------------------------------------------------------------

  [Fact]
  public async Task QuotingMarksEachSizeByItsOwnStockAndState()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateWithSizesAsync(admin, ("S", 3), ("M", 0), ("L", 2));
    await PatchVariantAsync(admin, id, await VariantIdAsync(admin, id, "L"), new { isActive = false });

    using var anonymous = factory.Anonymous();
    var response = await anonymous.PostAsJsonAsync("/api/v1/cart/quote", new
    {
      items = new[] { new { sku = $"{sku}-S", quantity = 1 }, new { sku = $"{sku}-M", quantity = 1 }, new { sku = $"{sku}-L", quantity = 1 } }
    });

    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    var lines = (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray()
        .ToDictionary(l => l.GetProperty("size").GetString()!, l => l.GetProperty("status").GetString());

    Assert.Equal("ok", lines["S"]);
    Assert.Equal("insufficient_stock", lines["M"]);
    Assert.Equal("unavailable", lines["L"]);
  }

  // ---- orden -----------------------------------------------------------------

  [Fact]
  public async Task BuyingASizeCopiesItIntoTheLineAndOnlyTouchesThatSize()
  {
    using var admin = await factory.AsAdminAsync();
    var (_, sku) = await CreateWithSizesAsync(admin, ("S", 3), ("M", 5));

    using var buyer = await factory.AsNewUserAsync();
    var response = await PlaceAsync(buyer, ($"{sku}-M", 2));

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var line = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
    Assert.Equal("M", line.GetProperty("size").GetString());

    Assert.Equal(3, await StockOfAsync($"{sku}-M"));
    Assert.Equal(3, await StockOfAsync($"{sku}-S"));
  }

  [Fact]
  public async Task BuyingAProductWithoutSizesStillWorksWithItsSku()
  {
    using var admin = await factory.AsAdminAsync();
    var (_, sku) = await CreateWithoutSizesAsync(admin, stock: 2);

    using var buyer = await factory.AsNewUserAsync();
    var response = await PlaceAsync(buyer, (sku, 1));

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var line = Assert.Single((await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("items").EnumerateArray());
    Assert.Equal(JsonValueKind.Null, line.GetProperty("size").ValueKind);
  }

  [Theory]
  [InlineData("missing", "sku_not_found")]
  [InlineData("inactive", "sku_unavailable")]
  [InlineData("short", "insufficient_stock")]
  public async Task AFailingLineHasAStableCodeTheSkuAndTakesNothing(string @case, string code)
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateWithSizesAsync(admin, ("L", 5), ("M", 1));

    if (@case == "inactive")
      await PatchVariantAsync(admin, id, await VariantIdAsync(admin, id, "M"), new { isActive = false });

    var failing = @case == "missing" ? $"{sku}-XXXL" : $"{sku}-M";

    // "-L" ordena antes que "-M" y que "-XXXL", y las líneas se apartan por SKU: la de L SÍ
    // se descuenta antes del fallo, así que lo que se prueba es que se deshace.
    using var buyer = await factory.AsNewUserAsync();
    var response = await PlaceAsync(buyer, ($"{sku}-L", 2), (failing, 3));

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
    var problem = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(code, problem.GetProperty("code").GetString());
    Assert.Equal(failing, problem.GetProperty("sku").GetString());

    Assert.Equal(5, await StockOfAsync($"{sku}-L"));
  }

  [Fact]
  public async Task TheLastUnitOfASizeIsSoldOnceUnderConcurrency()
  {
    using var admin = await factory.AsAdminAsync();
    var (_, sku) = await CreateWithSizesAsync(admin, ("S", 1), ("M", 10));

    var buyers = await Task.WhenAll(Enumerable.Range(0, 10).Select(_ => factory.AsNewUserAsync()));

    try
    {
      // Simultáneas, no una tras otra: es lo único que prueba el UPDATE condicional.
      var responses = await Task.WhenAll(buyers.Select(b => PlaceAsync(b, ($"{sku}-S", 1))));

      Assert.Equal(1, responses.Count(r => r.StatusCode == HttpStatusCode.Created));
      Assert.Equal(9, responses.Count(r => r.StatusCode == HttpStatusCode.Conflict));
      Assert.Equal(0, await StockOfAsync($"{sku}-S"));
      Assert.Equal(10, await StockOfAsync($"{sku}-M"));
    }
    finally
    {
      foreach (var buyer in buyers) buyer.Dispose();
    }
  }

  [Fact]
  public async Task AnAbandonedOrderReturnsTheStockToTheSizeItTook()
  {
    using var admin = await factory.AsAdminAsync();
    var (_, sku) = await CreateWithSizesAsync(admin, ("S", 4), ("M", 4));

    using var buyer = await factory.AsNewUserAsync();
    var order = await (await PlaceAsync(buyer, ($"{sku}-M", 3))).Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal(1, await StockOfAsync($"{sku}-M"));

    await BackdateAsync(order.GetProperty("publicId").GetGuid());
    await CollectAsync();

    Assert.Equal(4, await StockOfAsync($"{sku}-M"));
    Assert.Equal(4, await StockOfAsync($"{sku}-S"));
  }

  [Fact]
  public async Task ALineFromBeforeVariantsIsReadAndReturnedBySku()
  {
    using var admin = await factory.AsAdminAsync();
    var (_, sku) = await CreateWithoutSizesAsync(admin, stock: 5);

    using var buyer = await factory.AsNewUserAsync();
    var order = await (await PlaceAsync(buyer, (sku, 2))).Content.ReadFromJsonAsync<JsonElement>();
    var publicId = order.GetProperty("publicId").GetGuid();

    // Así estaban las líneas antes de planning/27: sin variante ni talla.
    using (var scope = factory.Services.CreateScope())
    {
      var db = scope.ServiceProvider.GetRequiredService<AppDbContext>();
      await db.OrderItems
          .Where(i => i.Order!.PublicId == publicId)
          .ExecuteUpdateAsync(s => s.SetProperty(i => i.VariantId, (int?)null).SetProperty(i => i.Size, (string?)null));
    }

    var read = await buyer.GetAsync($"/api/v1/order/{publicId}");
    Assert.Equal(HttpStatusCode.OK, read.StatusCode);

    await BackdateAsync(publicId);
    await CollectAsync();

    Assert.Equal(5, await StockOfAsync(sku));
  }

  // ---- administración --------------------------------------------------------

  [Fact]
  public async Task AnAdminAddsASizeWithTheDerivedSku()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateWithSizesAsync(admin, ("S", 1));

    var response = await admin.PostAsJsonAsync($"/api/v1/product/{id}/variants", new { size = "XL", stock = 4 });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    var variant = await response.Content.ReadFromJsonAsync<JsonElement>();
    Assert.Equal($"{sku}-XL", variant.GetProperty("sku").GetString());
    Assert.Equal(1, variant.GetProperty("position").GetInt32());
  }

  [Fact]
  public async Task ARepeatedSizeIsAConflict()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateWithSizesAsync(admin, ("M", 1));

    var response = await admin.PostAsJsonAsync($"/api/v1/product/{id}/variants", new { size = "m", stock = 4 });

    await AssertProblemAsync(response, HttpStatusCode.Conflict, "duplicate_size");
  }

  [Fact]
  public async Task SizesDoNotMixWithAnActiveVariantWithoutSize()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateWithoutSizesAsync(admin, stock: 3);

    var response = await admin.PostAsJsonAsync($"/api/v1/product/{id}/variants", new { size = "M", stock = 1 });
    await AssertProblemAsync(response, HttpStatusCode.Conflict, "variant_kind_mismatch");

    // Desactivando la variante sin talla, ya se puede.
    var single = Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}/variants")).EnumerateArray());
    await PatchVariantAsync(admin, id, single.GetProperty("id").GetInt32(), new { isActive = false });

    var retry = await admin.PostAsJsonAsync($"/api/v1/product/{id}/variants", new { size = "M", stock = 1 });
    Assert.Equal(HttpStatusCode.Created, retry.StatusCode);

    // Y reactivarla ahora mezclaría las dos formas.
    var reactivate = await admin.PatchAsJsonAsync(
        $"/api/v1/product/{id}/variants/{single.GetProperty("id").GetInt32()}", new { isActive = true });
    await AssertProblemAsync(reactivate, HttpStatusCode.Conflict, "variant_kind_mismatch");
  }

  [Fact]
  public async Task DeactivatingASoldSizeKeepsTheOrderReadable()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateWithSizesAsync(admin, ("S", 3));

    using var buyer = await factory.AsNewUserAsync();
    var order = await (await PlaceAsync(buyer, ($"{sku}-S", 1))).Content.ReadFromJsonAsync<JsonElement>();

    await PatchVariantAsync(admin, id, await VariantIdAsync(admin, id, "S"), new { isActive = false });

    var read = await buyer.GetFromJsonAsync<JsonElement>($"/api/v1/order/{order.GetProperty("publicId").GetGuid()}");
    Assert.Equal("S", Assert.Single(read.GetProperty("items").EnumerateArray()).GetProperty("size").GetString());
  }

  [Fact]
  public async Task RestockingWithAStaleVersionIsAPreconditionFailure()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateWithSizesAsync(admin, ("S", 3));

    var variant = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}/variants")).EnumerateArray().Single();
    var readVersion = variant.GetProperty("rowVersion").GetString();

    // Una venta entre la lectura y el PATCH también cambia la versión.
    using var buyer = await factory.AsNewUserAsync();
    Assert.Equal(HttpStatusCode.Created, (await PlaceAsync(buyer, ($"{sku}-S", 1))).StatusCode);

    using var request = new HttpRequestMessage(HttpMethod.Patch, $"/api/v1/product/{id}/variants/{variant.GetProperty("id").GetInt32()}")
    {
      Content = JsonContent.Create(new { stock = 20 })
    };
    request.Headers.IfMatch.Add(new EntityTagHeaderValue($"\"{readVersion}\""));

    Assert.Equal(HttpStatusCode.PreconditionFailed, (await admin.SendAsync(request)).StatusCode);
    Assert.Equal(2, await StockOfAsync($"{sku}-S"));

    // Sin If-Match se aplica.
    await PatchVariantAsync(admin, id, variant.GetProperty("id").GetInt32(), new { stock = 20 });
    Assert.Equal(20, await StockOfAsync($"{sku}-S"));
  }

  [Fact]
  public async Task VariantAdministrationIsForAdminsOnly()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateWithSizesAsync(admin, ("S", 3));

    using var user = await factory.AsNewUserAsync();
    Assert.Equal(HttpStatusCode.Forbidden, (await user.GetAsync($"/api/v1/product/{id}/variants")).StatusCode);

    using var anonymous = factory.Anonymous();
    Assert.Equal(HttpStatusCode.Unauthorized,
        (await anonymous.PostAsJsonAsync($"/api/v1/product/{id}/variants", new { size = "M", stock = 1 })).StatusCode);
  }

  // ---- regresiones de la revisión ----------------------------------------------

  [Fact]
  public async Task ReactivatingTheUnsizedVariantAndAddingASizeAtOnceNeverMixesThem()
  {
    // Sin el lock por producto, 23 de 25 intentos acababan con las dos formas activas.
    using var admin = await factory.AsAdminAsync();

    for (var attempt = 0; attempt < 10; attempt++)
    {
      var (id, _) = await CreateWithoutSizesAsync(admin, stock: 3);
      var unsized = Assert.Single((await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}/variants")).EnumerateArray())
          .GetProperty("id").GetInt32();
      await PatchVariantAsync(admin, id, unsized, new { isActive = false });

      await Task.WhenAll(
          admin.PatchAsJsonAsync($"/api/v1/product/{id}/variants/{unsized}", new { isActive = true }),
          admin.PostAsJsonAsync($"/api/v1/product/{id}/variants", new { size = "M", stock = 1 }));

      var active = (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}/variants"))
          .EnumerateArray().Where(v => v.GetProperty("isActive").GetBoolean()).ToList();

      var mixed = active.Any(v => v.GetProperty("size").ValueKind == JsonValueKind.Null)
                  && active.Any(v => v.GetProperty("size").ValueKind != JsonValueKind.Null);

      Assert.False(mixed, $"attempt {attempt}: unsized and sized variants are both active");
    }
  }

  [Fact]
  public async Task ANullSizeInTheListIsAValidationErrorNotA500()
  {
    using var admin = await factory.AsAdminAsync();

    var response = await PostProductAsync(admin, variants: [null!]);

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task AnEmptySkuMeansDeriveIt()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, sku) = await CreateWithSizesAsync(admin, ("S", 1));

    var response = await admin.PostAsJsonAsync($"/api/v1/product/{id}/variants", new { size = "XL", sku = "", stock = 4 });

    Assert.Equal(HttpStatusCode.Created, response.StatusCode);
    Assert.Equal($"{sku}-XL", (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("sku").GetString());
  }

  [Fact]
  public async Task StockHasAnUpperBoundSoTheProductTotalCannotOverflow()
  {
    // Dos tallas a int.MaxValue desbordaban la suma y la ficha, el listado y /stats daban 500.
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateWithSizesAsync(admin, ("S", 1));

    var response = await admin.PostAsJsonAsync($"/api/v1/product/{id}/variants", new { size = "M", stock = int.MaxValue });

    Assert.Equal(HttpStatusCode.BadRequest, response.StatusCode);
  }

  [Fact]
  public async Task RenamingTheSkuOfAProductWithoutSizesRenamesItsVariant()
  {
    using var admin = await factory.AsAdminAsync();
    var (id, _) = await CreateWithoutSizesAsync(admin, stock: 3);
    var renamed = $"R-{Guid.NewGuid():N}"[..20].ToUpperInvariant();

    Assert.Equal(HttpStatusCode.NoContent, (await admin.PatchAsJsonAsync($"/api/v1/product/{id}", new { sku = renamed })).StatusCode);

    var product = await PublicProductAsync(id);
    Assert.Equal(renamed, Assert.Single(product.GetProperty("variants").EnumerateArray()).GetProperty("sku").GetString());

    using var buyer = await factory.AsNewUserAsync();
    Assert.Equal(HttpStatusCode.Created, (await PlaceAsync(buyer, (renamed, 1))).StatusCode);
  }

  [Fact]
  public async Task AProductSkuCannotTakeTheSkuOfAnotherProductsVariant()
  {
    using var admin = await factory.AsAdminAsync();
    var (_, sized) = await CreateWithSizesAsync(admin, ("S", 1));
    var (other, _) = await CreateWithoutSizesAsync(admin, stock: 1);

    var response = await admin.PatchAsJsonAsync($"/api/v1/product/{other}", new { sku = $"{sized}-S" });

    Assert.Equal(HttpStatusCode.Conflict, response.StatusCode);
  }

  // ---- helpers -----------------------------------------------------------------

  private static async Task<HttpResponseMessage> PostProductAsync(
      HttpClient admin, int? stock = null, object?[]? variants = null, string? sku = null)
  {
    var category = await admin.PostAsJsonAsync("/api/v1/category",
        new { name = VersioningAndHealthTests.Unique("Cat") });
    var categoryId = int.Parse(category.Headers.Location!.Segments[^1]);

    return await admin.PostAsJsonAsync("/api/v1/product", new
    {
      name = VersioningAndHealthTests.Unique("Prenda"),
      price = 20m,
      sku = sku ?? $"V-{Guid.NewGuid():N}"[..20].ToUpperInvariant(),
      stock,
      variants,
      categoryId
    });
  }

  private static async Task<(int Id, string Sku)> CreateWithSizesAsync(
      HttpClient admin, params (string Size, int Stock)[] sizes)
  {
    var sku = $"V-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
    var response = await PostProductAsync(admin, variants: [.. sizes.Select(s => (object)new { size = s.Size, stock = s.Stock })], sku: sku);
    response.EnsureSuccessStatusCode();

    return (int.Parse(response.Headers.Location!.Segments[^1]), sku);
  }

  private static async Task<(int Id, string Sku)> CreateWithoutSizesAsync(HttpClient admin, int stock)
  {
    var sku = $"V-{Guid.NewGuid():N}"[..20].ToUpperInvariant();
    var response = await PostProductAsync(admin, stock: stock, sku: sku);
    response.EnsureSuccessStatusCode();

    return (int.Parse(response.Headers.Location!.Segments[^1]), sku);
  }

  private async Task<JsonElement> PublicProductAsync(int id)
  {
    using var anonymous = factory.Anonymous();
    return await anonymous.GetFromJsonAsync<JsonElement>($"/api/v1/product/{id}");
  }

  private static async Task<int> VariantIdAsync(HttpClient admin, int productId, string size)
      => (await admin.GetFromJsonAsync<JsonElement>($"/api/v1/product/{productId}/variants"))
          .EnumerateArray()
          .Single(v => v.GetProperty("size").GetString() == size)
          .GetProperty("id").GetInt32();

  private static async Task PatchVariantAsync(HttpClient admin, int productId, int variantId, object body)
  {
    var response = await admin.PatchAsJsonAsync($"/api/v1/product/{productId}/variants/{variantId}", body);
    Assert.Equal(HttpStatusCode.OK, response.StatusCode);
  }

  private static Task<HttpResponseMessage> PlaceAsync(HttpClient buyer, params (string Sku, int Quantity)[] lines)
      => buyer.PostAsJsonAsync("/api/v1/order", new
      {
        items = lines.Select(l => new { sku = l.Sku, quantity = l.Quantity }).ToArray(),
        customerName = "Cliente"
      });

  private static async Task AssertProblemAsync(HttpResponseMessage response, HttpStatusCode status, string code)
  {
    Assert.Equal(status, response.StatusCode);
    Assert.Equal(code, (await response.Content.ReadFromJsonAsync<JsonElement>()).GetProperty("code").GetString());
  }

  private async Task<int> StockOfAsync(string sku)
  {
    using var scope = factory.Services.CreateScope();

    return await scope.ServiceProvider.GetRequiredService<AppDbContext>()
        .ProductVariants.AsNoTracking()
        .Where(v => v.SKU == sku)
        .Select(v => v.Stock)
        .SingleAsync();
  }

  private async Task BackdateAsync(Guid publicId)
  {
    using var scope = factory.Services.CreateScope();

    // Fuera del árbol de expresión: dentro, EF no sabe traducir la resta.
    var when = DateTime.Now.AddDays(-1);

    await scope.ServiceProvider.GetRequiredService<AppDbContext>().Orders
        .Where(o => o.PublicId == publicId)
        .ExecuteUpdateAsync(s => s.SetProperty(o => o.PlacedAt, when));
  }

  private async Task CollectAsync()
  {
    using var scope = factory.Services.CreateScope();
    await scope.ServiceProvider.GetRequiredService<IAbandonedOrderCollector>().CollectAsync();
  }
}
