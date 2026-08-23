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


  // // // Auditoría automática -------------------------------------------------
  // Equivalente a @EnableJpaAuditing + @CreatedDate/@LastModifiedDate de Spring.
  // Antes cada repositorio asignaba UpdatedAt a mano (ProductRepository.BuyProduct);
  // ahora es imposible olvidarlo.

  public override Task<int> SaveChangesAsync(CancellationToken cancellationToken = default)
  {
    StampAuditFields();
    return base.SaveChangesAsync(cancellationToken);
  }

  public override int SaveChanges()
  {
    StampAuditFields();
    return base.SaveChanges();
  }

  /// <summary>
  /// Estampa <c>CreatedAt</c> / <c>UpdatedAt</c> sobre las entidades
  /// <see cref="IAuditable"/> rastreadas. Usa <c>DateTime.Now</c> (hora local),
  /// que es la decisión ya tomada en el proyecto (ver AGENTS/docs/02-repository.md).
  /// </summary>
  private void StampAuditFields()
  {
    var now = DateTime.Now;

    foreach (var entry in ChangeTracker.Entries<IAuditable>())
    {
      switch (entry.State)
      {
        case EntityState.Added:
          entry.Entity.CreatedAt = now;
          entry.Entity.UpdatedAt = null;
          break;

        case EntityState.Modified:
          entry.Entity.UpdatedAt = now;
          // CreatedAt es inmutable: aunque el update lo traiga, no se persiste.
          entry.Property(nameof(IAuditable.CreatedAt)).IsModified = false;
          break;
      }
    }
  }

}
