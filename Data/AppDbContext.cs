using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Models;

namespace ApiEcommerce.Data;

public class AppDbContext : DbContext
{

  public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
  {
  }

  // generar tabla en base a la clase con migraciones ---
  public DbSet<Category> Categories { get; set; }

  public DbSet<Product> Products { get; set; }

}
