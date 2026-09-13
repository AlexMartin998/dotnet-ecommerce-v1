using System.ComponentModel.DataAnnotations;
using System.Text.Json;
using ApiEcommerce.Data;
using ApiEcommerce.Features.Catalog.Dtos;

namespace ApiEcommerce.Tests.Data;


/// <summary>
/// El catálogo de demostración (<c>planning/26</c>). Se prueba el FICHERO, no la siembra: el
/// seeder guarda todo en un solo SaveChanges, así que un dato que viole un índice único o un
/// <c>MaxLength</c> no se ve hasta arrancar contra una base vacía, y ahí tumba el arranque.
/// </summary>
public class StorefrontCatalogSeedTests
{
  private const string Resource = "ApiEcommerce.Data.Seed.storefront-catalog.json";

  private static readonly JsonElement Catalog = LoadCatalog();

  private static JsonElement LoadCatalog()
  {
    using var stream = typeof(DataSeeder).Assembly.GetManifestResourceStream(Resource);
    Assert.NotNull(stream);
    return JsonDocument.Parse(stream).RootElement.Clone();
  }

  private static IEnumerable<JsonElement> Products => Catalog.GetProperty("products").EnumerateArray();

  private static List<string> Strings(JsonElement product, string property) =>
      product.GetProperty(property).EnumerateArray().Select(e => e.GetString()!).ToList();

  [Fact]
  public void HasTheFourCategoriesAndTheFiftyTwoProducts()
  {
    var categories = Catalog.GetProperty("categories").EnumerateArray()
        .Select(c => c.GetProperty("name").GetString()).ToList();

    Assert.Equal(["Shirts", "Pants", "Hoodies", "Hats"], categories);
    Assert.Equal(52, Products.Count());
    Assert.All(Products, p => Assert.Contains(p.GetProperty("category").GetString(), categories));
  }

  [Fact]
  public void EveryProductPassesTheValidationOfTheCreateEndpoint()
  {
    // Lo que el seeder mete por la puerta de atrás tiene que poder entrar por la de delante:
    // si no, el primer PATCH de un admin sobre un producto sembrado daría un 400 inexplicable.
    foreach (var p in Products)
    {
      var dto = new CreateProductDto
      {
        Name = p.GetProperty("name").GetString()!,
        Description = p.GetProperty("description").GetString(),
        Price = p.GetProperty("price").GetDecimal(),
        Slug = p.GetProperty("slug").GetString(),
        SKU = p.GetProperty("sku").GetString()!,
        Stock = p.GetProperty("stock").GetInt32(),
        Tags = Strings(p, "tags"),
        Sizes = Strings(p, "sizes"),
        CategoryId = 1
      };

      var errors = new List<ValidationResult>();
      Assert.True(Validator.TryValidateObject(dto, new ValidationContext(dto), errors, validateAllProperties: true),
          $"{dto.Name}: {string.Join("; ", errors.Select(e => e.ErrorMessage))}");
    }
  }

  [Fact]
  public void SkusAndSlugsAreUnique()
  {
    // Son los dos índices únicos de Product: un repetido revienta el único SaveChanges.
    Assert.Equal(52, Products.Select(p => p.GetProperty("sku").GetString()).Distinct().Count());
    Assert.Equal(52, Products.Select(p => p.GetProperty("slug").GetString()).Distinct().Count());
  }

  [Fact]
  public void TheGenderOfTheFrontTravelsAsATag()
  {
    string[] genders = ["men", "women", "kid", "unisex"];

    Assert.All(Products, p => Assert.Single(Strings(p, "tags"), genders.Contains));
  }

  [Fact]
  public void EveryImageShipsWithTheBuildAndFitsTheStorageLimit()
  {
    var folder = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "images");

    foreach (var image in Products.SelectMany(p => Strings(p, "images")))
    {
      var file = new FileInfo(Path.Combine(folder, image));

      Assert.True(file.Exists, $"{image} is not in the build output");
      // Storage:MaxBytes por defecto: más grande y LocalFileStorage la rechazaría al sembrar.
      Assert.InRange(file.Length, 1, 2 * 1024 * 1024);

      // La firma tiene que coincidir con la extensión, o LocalFileStorage la rechaza y el
      // arranque muere. Teslo servía WebP con extensión .jpg: 83 de 104.
      var header = new byte[12];
      using (var stream = file.OpenRead()) stream.ReadExactly(header);

      var isWebp = header[..4].SequenceEqual("RIFF"u8.ToArray()) && header[8..12].SequenceEqual("WEBP"u8.ToArray());
      var isJpeg = header[0] == 0xFF && header[1] == 0xD8 && header[2] == 0xFF;

      Assert.True(file.Extension switch { ".webp" => isWebp, ".jpg" => isJpeg, _ => false },
          $"{image}: its content does not match its extension");
    }
  }
}
