using ApiEcommerce.Shared.Auth;
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
      await SeedCatalogAsync(db, logger, ct);
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

  private static async Task SeedCatalogAsync(AppDbContext db, ILogger logger, CancellationToken ct)
  {
    if (await db.Categories.AnyAsync(ct))
    {
      logger.LogInformation("Catalog already seeded, skipping");
      return;
    }

    var categories = new List<Category>
    {
      new() { Name = "Electronica", Description = "Telefonos, portatiles y accesorios" },
      new() { Name = "Ropa", Description = "Prendas y complementos" },
      new() { Name = "Hogar", Description = "Menaje y decoracion" },
      new() { Name = "Deportes", Description = "Material deportivo" },
      new() { Name = "Libros", Description = "Papel y digital" }
    };

    db.Categories.AddRange(categories);

    // SaveChanges antes de crear los productos: es lo que asigna los Id reales.
    await db.SaveChangesAsync(ct);

    var electronica = categories[0].Id;
    var ropa = categories[1].Id;
    var hogar = categories[2].Id;

    // El slug se deriva del nombre, igual que en ProductMapper: si el seeder los inventara
    // por su cuenta, el catalogo sembrado no se pareceria al que crea la API.
    db.Products.AddRange(
        Seeded("Portatil 14\"", "8 GB RAM, 512 GB SSD", 899.99m, "ELEC-LAP-001", 12, electronica, ["electronica", "portatiles"]),
        Seeded("Telefono X", "128 GB, pantalla 6.1\"", 649.00m, "ELEC-PHO-001", 30, electronica, ["electronica", "telefonos"]),
        Seeded("Auriculares BT", "Cancelacion de ruido", 129.50m, "ELEC-AUD-001", 55, electronica, ["electronica", "audio"]),
        Seeded("Camiseta basica", "Algodon organico", 19.99m, "ROPA-CAM-001", 120, ropa, ["ropa", "unisex"], ["S", "M", "L", "XL"]),
        Seeded("Sudadera capucha", "Unisex", 39.90m, "ROPA-SUD-001", 60, ropa, ["ropa", "unisex"], ["M", "L", "XL"]),
        Seeded("Cafetera italiana", "6 tazas, aluminio", 24.95m, "HOGA-CAF-001", 40, hogar, ["hogar", "cocina"]),
        Seeded("Juego de sabanas", "150x200, percal", 44.00m, "HOGA-SAB-001", 25, hogar, ["hogar", "dormitorio"]));

    await db.SaveChangesAsync(ct);

    logger.LogInformation("Seeded {Categories} categories and 7 products", categories.Count);
  }

  private static Product Seeded(
      string name, string description, decimal price, string sku, int stock, int categoryId,
      List<string> tags, List<string>? sizes = null) => new()
      {
        Name = name,
        Slug = Slugs.From(name)!,
        Description = description,
        Price = price,
        SKU = sku,
        Stock = stock,
        CategoryId = categoryId,
        Tags = tags,
        Sizes = sizes ?? []
      };
}
