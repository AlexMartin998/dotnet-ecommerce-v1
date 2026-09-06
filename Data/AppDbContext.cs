using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Features.Ordering.Models;

namespace ApiEcommerce.Data;


/// <summary>
/// Contexto de EF Core del proyecto, con el dominio y las tablas de Identity.
/// </summary>
/// <remarks>
/// Hereda de <see cref="IdentityDbContext{TUser}"/> y no de <c>DbContext</c> para que las
/// tablas <c>AspNet*</c> compartan contexto y transacción con el dominio. El único
/// usuario es <see cref="ApplicationUser"/>.
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
  /// Comandos ya ejecutados; se escriben en la misma transacción que su efecto y son
  /// la garantía de idempotencia de las operaciones de negocio.
  /// </summary>
  public DbSet<ExecutedCommand> ExecutedCommands { get; set; }

  /// <summary>
  /// Refresh tokens emitidos. Revocar y emitir el siguiente ocurren en la misma
  /// transacción, que es lo que permite cortar una sesión.
  /// </summary>
  public DbSet<RefreshToken> RefreshTokens { get; set; }

  public DbSet<Order> Orders { get; set; }

  public DbSet<OrderItem> OrderItems { get; set; }


  /// <summary>
  /// Configura el modelo: índices, secuencias y precisión de los importes.
  /// </summary>
  /// <remarks>
  /// La llamada a <c>base.OnModelCreating</c> es obligatoria: es quien mapea las tablas
  /// de Identity. Omitirla compila y falla al generar la migración.
  /// </remarks>
  protected override void OnModelCreating(ModelBuilder modelBuilder)
  {
    base.OnModelCreating(modelBuilder);

    // La secuencia la asigna SQL Server: es el único árbitro de orden que no depende
    // del reloj de cada réplica.
    modelBuilder.Entity<OutboxMessage>()
        .Property(m => m.Sequence)
        .ValueGeneratedOnAdd()
        .UseIdentityColumn();

    // Índice del publicador: filtrado por pendientes para que no crezca con la tabla, y
    // por Sequence, que es el orden real de drenaje. `Attempts` va en el INCLUDE porque
    // el publicador filtra por él y si no habría un key lookup por fila recorrida.
    modelBuilder.Entity<OutboxMessage>()
        .HasIndex(m => m.Sequence)
        .IncludeProperties(m => m.Attempts)
        .HasFilter("[ProcessedAt] IS NULL")
        .HasDatabaseName("IX_OutboxMessages_Pending");

    // La purga borra por fecha de procesado; sin índice sería un scan en cada pasada.
    modelBuilder.Entity<OutboxMessage>()
        .HasIndex(m => m.ProcessedAt)
        .HasDatabaseName("IX_OutboxMessages_ProcessedAt");

    modelBuilder.Entity<ProcessedMessage>()
        .HasIndex(m => m.ProcessedAt)
        .HasDatabaseName("IX_ProcessedMessages_ProcessedAt");

    // Misma razón que en ProcessedMessages: la purga borra por fecha.
    modelBuilder.Entity<ExecutedCommand>()
        .HasIndex(c => c.ExecutedAt)
        .HasDatabaseName("IX_ExecutedCommands_ExecutedAt");

    // Único: por aquí se busca en cada refresh, y evita dos tokens con la misma huella.
    modelBuilder.Entity<RefreshToken>()
        .HasIndex(t => t.TokenHash)
        .IsUnique()
        .HasDatabaseName("IX_RefreshTokens_TokenHash");

    // Por aquí se revoca la familia entera al detectar un reuso, que urge.
    modelBuilder.Entity<RefreshToken>()
        .HasIndex(t => t.FamilyId)
        .HasDatabaseName("IX_RefreshTokens_FamilyId");

    // La purga borra por fecha de expiración.
    modelBuilder.Entity<RefreshToken>()
        .HasIndex(t => t.ExpiresAt)
        .HasDatabaseName("IX_RefreshTokens_ExpiresAt");

    // ---- órdenes ------------------------------------------------------------

    // Secuencia y no MAX()+1: eso último es un leer-y-escribir y dos compras simultáneas
    // se llevarían el mismo número, que además es único.
    modelBuilder.HasSequence<long>(Features.Ordering.Repository.OrderRepository.NumberSequence)
        .StartsAt(1)
        .IncrementsBy(1);

    // Único: es el número que cita el cliente, y repetido lo vuelve inservible.
    modelBuilder.Entity<Order>()
        .HasIndex(o => o.Number)
        .IsUnique()
        .HasDatabaseName("IX_Orders_Number");

    // Por aquí consulta "mis órdenes", la lectura más frecuente del slice.
    modelBuilder.Entity<Order>()
        .HasIndex(o => new { o.BuyerUserId, o.PlacedAt })
        .HasDatabaseName("IX_Orders_Buyer_PlacedAt");

    // Precisión explícita: la convención de EF ya da decimal(18,2), pero dejarlo implícito
    // haría que un cambio de convención moviese dinero sin que nadie lo note.
    foreach (var money in new[] { "Subtotal", "Discount", "Tax", "Shipping", "Total" })
      modelBuilder.Entity<Order>().Property(money).HasPrecision(18, 2);

    modelBuilder.Entity<OrderItem>().Property(i => i.UnitPrice).HasPrecision(18, 2);
    modelBuilder.Entity<OrderItem>().Property(i => i.LineTotal).HasPrecision(18, 2);

    // Borrar una orden se lleva sus líneas: no tienen sentido sueltas.
    modelBuilder.Entity<Order>()
        .HasMany(o => o.Items)
        .WithOne(i => i.Order!)
        .HasForeignKey(i => i.OrderId)
        .OnDelete(DeleteBehavior.Cascade);
  }


  // Auditoría automática: equivale a @EnableJpaAuditing + @CreatedDate/@LastModifiedDate.

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
  /// <see cref="IAuditable"/> rastreadas, con <c>DateTime.Now</c> (hora local).
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
