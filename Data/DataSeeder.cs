using System.Text.Json;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Storage;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Data;


/// <summary>
/// Siembra roles, el usuario administrador y (opcionalmente) datos de demostración.
/// </summary>
/// <remarks>
/// Roles y usuarios se crean con <c>RoleManager</c>/<c>UserManager</c> y nunca con
/// <c>INSERT</c>: un insert a mano se salta el <c>SecurityStamp</c> y deja sin efecto el
/// lockout y la invalidación de credenciales de Identity.
/// </remarks>
public static class DataSeeder
{
  /// <summary>Siembra lo que falte; es idempotente, cada bloque comprueba antes de insertar.</summary>
  public static async Task SeedAsync(IServiceProvider services, CancellationToken ct = default)
  {
    var options = services.GetRequiredService<IOptions<SeedOptions>>().Value;
    var logger = services.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(DataSeeder));

    if (!options.Enabled)
    {
      logger.LogInformation("Seeding disabled (Seed:Enabled = false)");
      return;
    }

    var db = services.GetRequiredService<AppDbContext>();
    var roleManager = services.GetRequiredService<RoleManager<IdentityRole>>();
    var userManager = services.GetRequiredService<UserManager<ApplicationUser>>();

    await SeedRolesAsync(roleManager, logger, ct);
    await SeedAdminAsync(userManager, options, logger);

    if (options.IncludeDemoData)
      await SeedCatalogAsync(db, services.GetRequiredService<IFileStorage>(), logger, ct);
  }

  // ---- roles --------------------------------------------------------------

  private static async Task SeedRolesAsync(
      RoleManager<IdentityRole> roleManager, ILogger logger, CancellationToken ct)
  {
    foreach (var role in Roles.All)
    {
      if (await roleManager.RoleExistsAsync(role)) continue;

      var result = await roleManager.CreateAsync(new IdentityRole(role));

      if (result.Succeeded)
        logger.LogInformation("Seeded role {Role}", role);
      else
        logger.LogError("Could not seed role {Role}: {Errors}",
            role, string.Join("; ", result.Errors.Select(e => e.Description)));
    }
  }

  // ---- administrador ------------------------------------------------------

  private static async Task SeedAdminAsync(
      UserManager<ApplicationUser> userManager, SeedOptions options, ILogger logger)
  {
    if (await userManager.FindByNameAsync(options.AdminUsername) is not null)
    {
      logger.LogInformation("Admin user {Username} already exists", options.AdminUsername);
      return;
    }

    var admin = new ApplicationUser
    {
      UserName = options.AdminUsername,
      Email = options.AdminEmail,
      EmailConfirmed = true,
      Name = "Administrator"
    };

    var created = await userManager.CreateAsync(admin, options.AdminPassword);

    if (!created.Succeeded)
    {
      logger.LogError("Could not seed admin user: {Errors}",
          string.Join("; ", created.Errors.Select(e => e.Description)));
      return;
    }

    await userManager.AddToRoleAsync(admin, Roles.Admin);
    logger.LogInformation("Seeded admin user {Username}", options.AdminUsername);
  }

  // ---- catálogo de demostración -------------------------------------------

  // Recurso embebido y no fichero suelto: el catálogo se siembra también en un despliegue
  // que no lleve la carpeta de imágenes (ver el .csproj).
  private const string CatalogResource = "ApiEcommerce.Data.Seed.storefront-catalog.json";

  private static readonly string ImagesFolder = Path.Combine(AppContext.BaseDirectory, "Data", "Seed", "images");

  private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

  /// <summary>
  /// Siembra el catálogo de Teslo Shop (<c>planning/26</c>): 4 categorías, 52 productos con
  /// stock por talla (<c>planning/27</c>) y sus imágenes. No hace nada si ya hay alguna categoría.
  /// </summary>
  /// <remarks>
  /// Las imágenes entran por <see cref="IFileStorage"/>, el mismo camino que una subida del
  /// admin: pasan su validación, reciben nombre del servidor y mañana irían a S3 sin tocar
  /// esto. Copiarlas a mano a <c>wwwroot</c> ataría el seeder al disco local.
  /// </remarks>
  private static async Task SeedCatalogAsync(
      AppDbContext db, IFileStorage storage, ILogger logger, CancellationToken ct)
  {
    if (await db.Categories.AnyAsync(ct))
    {
      logger.LogInformation("Catalog already seeded, skipping");
      return;
    }

    var catalog = await ReadCatalogAsync(ct);

    var categories = catalog.Categories.ToDictionary(
        c => c.Name,
        c => new Category { Name = c.Name, Slug = Slugs.From(c.Name)!, Description = c.Description });

    var withImages = Directory.Exists(ImagesFolder);

    if (!withImages)
      logger.LogWarning("Demo images folder {Folder} not found: seeding products without images", ImagesFolder);

    var products = new List<Product>(catalog.Products.Count);

    foreach (var item in catalog.Products)
    {
      var product = new Product
      {
        Name = item.Name,
        // El slug del origen, no Slugs.From(Name): el apóstrofo de "Men's" daría "men-s-…".
        Slug = item.Slug,
        Description = item.Description,
        Price = item.Price,
        SKU = item.Sku,
        Category = categories[item.Category],
        Tags = item.Tags,
        // Las mismas dos formas que el alta por la API (ProductMapper): una variante por
        // talla con SKU {sku}-{talla}, o una sola sin talla con el SKU del producto.
        Variants = item.Variants is { Count: > 0 }
            ? [.. item.Variants.Select((v, index) => new ProductVariant
              {
                Size = v.Size,
                SKU = VariantSkus.For(item.Sku, v.Size),
                Stock = v.Stock,
                Position = index
              })]
            : [new ProductVariant { SKU = item.Sku, Stock = item.Stock ?? 0, Position = 0 }]
      };

      if (withImages)
      {
        for (var position = 0; position < item.Images.Count; position++)
          product.Images.Add(new ProductImage
          {
            Url = await StoreImageAsync(storage, item.Images[position], ct),
            Position = position
          });
      }

      products.Add(product);
    }

    // UN solo SaveChanges: o entra el catálogo entero o nada. Al revés, un fallo a mitad
    // dejaría categorías y el `AnyAsync` de arriba no volvería a intentarlo nunca. El precio
    // es que un fallo aquí deja imágenes huérfanas en el almacenamiento, que en un seeder
    // de desarrollo se acepta.
    db.Categories.AddRange(categories.Values);
    db.Products.AddRange(products);
    await db.SaveChangesAsync(ct);

    logger.LogInformation(
        "Seeded {Categories} categories, {Products} products, {Variants} variants and {Images} images",
        categories.Count, products.Count, products.Sum(p => p.Variants.Count), products.Sum(p => p.Images.Count));
  }

  private static async Task<SeedCatalog> ReadCatalogAsync(CancellationToken ct)
  {
    await using var stream = typeof(DataSeeder).Assembly.GetManifestResourceStream(CatalogResource)
        ?? throw new InvalidOperationException($"Embedded resource {CatalogResource} not found.");

    return await JsonSerializer.DeserializeAsync<SeedCatalog>(stream, JsonOptions, ct)
        ?? throw new InvalidOperationException($"Embedded resource {CatalogResource} is empty.");
  }

  private static async Task<string> StoreImageAsync(IFileStorage storage, string fileName, CancellationToken ct)
  {
    await using var content = File.OpenRead(Path.Combine(ImagesFolder, fileName));

    // ⚠️ 83 de las 104 imágenes de Teslo eran WebP con extensión .jpg: el navegador las
    // pinta igual, pero LocalFileStorage compara la firma con la extensión y las rechaza.
    // Se renombraron al traerlas; StorefrontCatalogSeedTests vigila que no vuelva a pasar.
    var contentType = Path.GetExtension(fileName) == ".webp" ? "image/webp" : "image/jpeg";

    return await storage.SaveImageAsync(
        new FileUpload(content, fileName, contentType, content.Length), ct);
  }

  // La forma del JSON. Privada: es el formato de un fichero de semillas, no un contrato.
  private sealed record SeedCatalog(List<SeedCategory> Categories, List<SeedProduct> Products);

  private sealed record SeedCategory(string Name, string? Description);

  // `Stock` solo en los productos sin tallas; los demás lo llevan por talla en `Variants`.
  private sealed record SeedProduct(
      string Name, string Slug, string Sku, string? Description, decimal Price, int? Stock,
      string Category, List<string> Tags, List<SeedVariant> Variants, List<string> Images);

  private sealed record SeedVariant(string Size, int Stock);

  // Catálogo de demostración anterior (hasta planning/26): siete productos inventados, sin
  // imágenes, que no dejaban ver el front como una tienda. Se conserva como registro.
  //
  // private static async Task SeedCatalogAsync(AppDbContext db, ILogger logger, CancellationToken ct)
  // {
  //   if (await db.Categories.AnyAsync(ct))
  //   {
  //     logger.LogInformation("Catalog already seeded, skipping");
  //     return;
  //   }
  //
  //   var categories = new List<Category>
  //   {
  //     new() { Name = "Electronica", Slug = Slugs.From("Electronica")!, Description = "Telefonos, portatiles y accesorios" },
  //     new() { Name = "Ropa", Slug = Slugs.From("Ropa")!, Description = "Prendas y complementos" },
  //     new() { Name = "Hogar", Slug = Slugs.From("Hogar")!, Description = "Menaje y decoracion" },
  //     new() { Name = "Deportes", Slug = Slugs.From("Deportes")!, Description = "Material deportivo" },
  //     new() { Name = "Libros", Slug = Slugs.From("Libros")!, Description = "Papel y digital" }
  //   };
  //
  //   db.Categories.AddRange(categories);
  //
  //   // SaveChanges antes de crear los productos: es lo que asigna los Id reales.
  //   await db.SaveChangesAsync(ct);
  //
  //   var electronica = categories[0].Id;
  //   var ropa = categories[1].Id;
  //   var hogar = categories[2].Id;
  //
  //   // El slug se deriva del nombre, igual que en ProductMapper: si el seeder los inventara
  //   // por su cuenta, el catalogo sembrado no se pareceria al que crea la API.
  //   db.Products.AddRange(
  //       Seeded("Portatil 14\"", "8 GB RAM, 512 GB SSD", 899.99m, "ELEC-LAP-001", 12, electronica, ["electronica", "portatiles"]),
  //       Seeded("Telefono X", "128 GB, pantalla 6.1\"", 649.00m, "ELEC-PHO-001", 30, electronica, ["electronica", "telefonos"]),
  //       Seeded("Auriculares BT", "Cancelacion de ruido", 129.50m, "ELEC-AUD-001", 55, electronica, ["electronica", "audio"]),
  //       Seeded("Camiseta basica", "Algodon organico", 19.99m, "ROPA-CAM-001", 120, ropa, ["ropa", "unisex"], ["S", "M", "L", "XL"]),
  //       Seeded("Sudadera capucha", "Unisex", 39.90m, "ROPA-SUD-001", 60, ropa, ["ropa", "unisex"], ["M", "L", "XL"]),
  //       Seeded("Cafetera italiana", "6 tazas, aluminio", 24.95m, "HOGA-CAF-001", 40, hogar, ["hogar", "cocina"]),
  //       Seeded("Juego de sabanas", "150x200, percal", 44.00m, "HOGA-SAB-001", 25, hogar, ["hogar", "dormitorio"]));
  //
  //   await db.SaveChangesAsync(ct);
  //
  //   logger.LogInformation("Seeded {Categories} categories and 7 products", categories.Count);
  // }
  //
  // private static Product Seeded(
  //     string name, string description, decimal price, string sku, int stock, int categoryId,
  //     List<string> tags, List<string>? sizes = null) => new()
  //     {
  //       Name = name,
  //       Slug = Slugs.From(name)!,
  //       Description = description,
  //       Price = price,
  //       SKU = sku,
  //       Stock = stock,
  //       CategoryId = categoryId,
  //       Tags = tags,
  //       Sizes = sizes ?? []
  //     };
}
