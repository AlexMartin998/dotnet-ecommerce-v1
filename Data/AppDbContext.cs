using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Models;

namespace ApiEcommerce.Data;


/// <summary>
/// Contexto de EF Core. Hereda de <see cref="IdentityDbContext{TUser}"/> y no de
/// <c>DbContext</c>: eso añade las 7 tablas <c>AspNet*</c> (usuarios, roles, claims,
/// logins, tokens) al mismo contexto y a la misma transacción que el dominio.
/// </summary>
/// <remarks>
/// El contexto de referencia del curso mantenía <b>dos</b> tablas de usuarios (una
/// legacy <c>Users</c> y las de Identity) y declaraba un <c>DbSet&lt;User&gt; Users</c>
/// que <b>ocultaba</b> el <c>Users</c> de <see cref="IdentityDbContext{TUser}"/>.
/// Resultado: las comprobaciones de unicidad consultaban una tabla vacía y siempre
/// devolvían "libre". Aquí solo existe <see cref="ApplicationUser"/>.
/// </remarks>
public class AppDbContext : IdentityDbContext<ApplicationUser>
{

  public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
  {
  }

  // generar tabla en base a la clase con migraciones ---
  public DbSet<Category> Categories { get; set; }

  public DbSet<Product> Products { get; set; }

  /// <summary>Eventos de dominio pendientes de publicar (ver <see cref="OutboxMessage"/>).</summary>
  public DbSet<OutboxMessage> OutboxMessages { get; set; }

  /// <summary>Mensajes ya consumidos, para que el consumidor sea idempotente.</summary>
  public DbSet<ProcessedMessage> ProcessedMessages { get; set; }


  /// <summary>
  /// <c>base.OnModelCreating</c> es <b>obligatorio</b>: es quien mapea las tablas de
  /// Identity y sus índices únicos. Omitirlo compila y luego falla en la migración.
  /// </summary>
  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    // Índice filtrado: el publicador solo consulta los pendientes, y en una tabla
    // que crece sin parar un índice sobre TODAS las filas sería cada vez más caro.
    // Aquí solo se indexa lo que de verdad se busca.
    modelBuilder.Entity<OutboxMessage>()
        .HasIndex(m => m.OccurredAt)
        .HasFilter("[ProcessedAt] IS NULL")
        .HasDatabaseName("IX_OutboxMessages_Pending");
  }


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
