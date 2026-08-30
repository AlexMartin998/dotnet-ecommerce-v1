using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Features.Catalog.Models;

namespace ApiEcommerce.Data;


/// <summary>
/// Siembra roles, el usuario administrador y (opcionalmente) datos de demostración.
/// </summary>
/// <remarks>
/// <para>
/// Se ejecuta desde <c>Program.cs</c> dentro de un <b>scope propio</b>: el contenedor
/// raíz no puede resolver servicios <c>Scoped</c> como <c>AppDbContext</c> o
/// <c>UserManager</c>, y hacerlo lanza en el arranque.
/// </para>
/// <para>
/// <b>Roles y usuarios se crean con <c>RoleManager</c>/<c>UserManager</c>, nunca con
/// <c>INSERT</c> directo.</b> Un insert a mano se salta el <c>SecurityStamp</c> y el
/// <c>ConcurrencyStamp</c>, y sin <c>SecurityStamp</c> el lockout y la invalidación de
/// credenciales de Identity dejan de funcionar — un fallo que no se ve hasta que hace
/// falta.
/// </para>
/// <para>
/// Es <b>idempotente</b>: cada bloque comprueba antes de insertar, así que arrancar
/// dos veces no duplica nada.
/// </para>
/// </remarks>
public static class DataSeeder
{
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

    // SaveChanges ANTES de crear los productos: es lo que asigna los Id reales.
    // El seeder de referencia hacía Categories.Find(1) sobre categorías todavía no
    // persistidas y solo funcionaba por accidente, si el IDENTITY empezaba en 1.
    await db.SaveChangesAsync(ct);

    var electronica = categories[0].Id;
    var ropa = categories[1].Id;
    var hogar = categories[2].Id;

    db.Products.AddRange(
        new Product { Name = "Portatil 14\"", Description = "8 GB RAM, 512 GB SSD", Price = 899.99m, SKU = "ELEC-LAP-001", Stock = 12, CategoryId = electronica },
        new Product { Name = "Telefono X", Description = "128 GB, pantalla 6.1\"", Price = 649.00m, SKU = "ELEC-PHO-001", Stock = 30, CategoryId = electronica },
        new Product { Name = "Auriculares BT", Description = "Cancelacion de ruido", Price = 129.50m, SKU = "ELEC-AUD-001", Stock = 55, CategoryId = electronica },
        new Product { Name = "Camiseta basica", Description = "Algodon organico", Price = 19.99m, SKU = "ROPA-CAM-001", Stock = 120, CategoryId = ropa },
        new Product { Name = "Sudadera capucha", Description = "Unisex", Price = 39.90m, SKU = "ROPA-SUD-001", Stock = 60, CategoryId = ropa },
        new Product { Name = "Cafetera italiana", Description = "6 tazas, aluminio", Price = 24.95m, SKU = "HOGA-CAF-001", Stock = 40, CategoryId = hogar },
        new Product { Name = "Juego de sabanas", Description = "150x200, percal", Price = 44.00m, SKU = "HOGA-SAB-001", Stock = 25, CategoryId = hogar });

    await db.SaveChangesAsync(ct);

    logger.LogInformation("Seeded {Categories} categories and 7 products", categories.Count);
  }
}
