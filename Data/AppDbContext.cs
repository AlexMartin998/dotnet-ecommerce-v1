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
using ApiEcommerce.Features.Payments.Models;

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

  /// <summary>Imágenes públicas de producto, en el orden en que se enseñan.</summary>
  public DbSet<ProductImage> ProductImages { get; set; }

  public DbSet<ProductVariant> ProductVariants { get; set; }

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

  public DbSet<Payment> Payments { get; set; }

  /// <summary>Dedupe del webhook: las pasarelas reenvían por diseño.</summary>
  public DbSet<ProcessedWebhookEvent> ProcessedWebhookEvents { get; set; }


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

    // Por aquí se cortan todas las sesiones de un usuario: cambio de contraseña, logout-all y bloqueo.
    modelBuilder.Entity<RefreshToken>()
        .HasIndex(t => new { t.UserId, t.RevokedAt })
        .HasDatabaseName("IX_RefreshTokens_User_RevokedAt");

    // ---- cuentas ------------------------------------------------------------

    // Único en la BASE: `RequireUniqueEmail` solo valida en C#, y ahí cabe una carrera.
    modelBuilder.Entity<ApplicationUser>()
        .HasIndex(u => u.NormalizedEmail)
        .IsUnique()
        .HasFilter("[NormalizedEmail] IS NOT NULL")   // SQL Server admite un solo NULL por índice único
        .HasDatabaseName("EmailIndex");

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

    // Único: es lo que citan las rutas públicas en lugar de la clave primaria.
    modelBuilder.Entity<Order>()
        .HasIndex(o => o.PublicId)
        .IsUnique()
        .HasDatabaseName("IX_Orders_PublicId");

    // Por aquí consulta "mis órdenes", la lectura más frecuente del slice.
    modelBuilder.Entity<Order>()
        .HasIndex(o => new { o.BuyerUserId, o.PlacedAt })
        .HasDatabaseName("IX_Orders_Buyer_PlacedAt");

    // Por aquí consulta el listado de administración, que ordena por PlacedAt sin filtrar
    // por comprador: sin este índice es un recorrido completo mas ordenación.
    modelBuilder.Entity<Order>()
        .HasIndex(o => o.PlacedAt)
        .HasDatabaseName("IX_Orders_PlacedAt");

    // ---- pagos ---------------------------------------------------------------

    modelBuilder.HasSequence<long>(Features.Payments.Repository.PaymentRepository.ReferenceSequence)
        .StartsAt(1)
        .IncrementsBy(1);

    // Único: es la referencia que cita el cliente.
    modelBuilder.Entity<Payment>()
        .HasIndex(p => p.Reference)
        .IsUnique()
        .HasDatabaseName("IX_Payments_Reference");

    modelBuilder.Entity<Payment>()
        .HasIndex(p => p.PublicId)
        .IsUnique()
        .HasDatabaseName("IX_Payments_PublicId");

    // Único y filtrado: es lo que ata un webhook a SU fila, y repetido movería dos pagos.
    // Filtrado porque la fila existe antes de que la pasarela conteste.
    modelBuilder.Entity<Payment>()
        .HasIndex(p => new { p.Provider, p.ProviderPaymentId })
        .IsUnique()
        .HasFilter("[ProviderPaymentId] IS NOT NULL")
        .HasDatabaseName("IX_Payments_Provider_ProviderPaymentId");

    // Por aquí consulta "mis pagos", y tambien "¿esta orden ya tiene pago?".
    modelBuilder.Entity<Payment>()
        .HasIndex(p => new { p.BuyerUserId, p.CreatedAt })
        .HasDatabaseName("IX_Payments_Buyer_CreatedAt");

    modelBuilder.Entity<Payment>()
        .HasIndex(p => p.OrderId)
        .HasDatabaseName("IX_Payments_OrderId");

    modelBuilder.Entity<Payment>().Property(p => p.Amount).HasPrecision(18, 2);

    // Como string y no como int: el numero del enum no dice nada al mirar la tabla, y
    // reordenar el enum reescribiria el significado de las filas ya guardadas.
    modelBuilder.Entity<Payment>().Property(p => p.Provider).HasConversion<string>().HasMaxLength(30);
    modelBuilder.Entity<Payment>().Property(p => p.Status).HasConversion<string>().HasMaxLength(30);

    // ---- catálogo ----------------------------------------------------------

    // Filtro global: un producto retirado desaparece de TODA consulta, incluidas las del
    // CRUD genérico. Sin esto habría que acordarse de excluirlo en cada repositorio.
    modelBuilder.Entity<Product>().HasQueryFilter(p => p.DeletedAt == null);

    // Índices únicos FILTRADOS: con el borrado lógico, un producto retirado seguiría
    // bloqueando su SKU y su slug para siempre, y ninguno de los dos es reutilizable a mano.
    modelBuilder.Entity<Product>()
        .HasIndex(p => p.SKU)
        .IsUnique()
        .HasFilter("[DeletedAt] IS NULL");

    modelBuilder.Entity<Product>()
        .HasIndex(p => p.Slug)
        .IsUnique()
        .HasFilter("[DeletedAt] IS NULL");

    // Borrar el producto se lleva sus imágenes; las órdenes no, que es de lo que protege
    // el borrado lógico.
    modelBuilder.Entity<Product>()
        .HasMany(p => p.Images)
        .WithOne(i => i.Product!)
        .HasForeignKey(i => i.ProductId)
        .OnDelete(DeleteBehavior.Cascade);

    // El mismo filtro que el producto: sin el, consultar ProductImages por su cuenta
    // devolveria las imagenes de productos retirados, y EF avisa de ello al arrancar.
    modelBuilder.Entity<ProductImage>()
        .HasQueryFilter(i => i.Product!.DeletedAt == null);

    modelBuilder.Entity<ProductImage>()
        .HasIndex(i => new { i.ProductId, i.Position })
        .HasDatabaseName("IX_ProductImages_ProductId_Position");

    // Variantes: mismo trato que las imágenes, salvo que estas sí se venden.
    modelBuilder.Entity<Product>()
        .HasMany(p => p.Variants)
        .WithOne(v => v.Product!)
        .HasForeignKey(v => v.ProductId)
        .OnDelete(DeleteBehavior.Cascade);

    modelBuilder.Entity<ProductVariant>()
        .HasQueryFilter(v => v.Product!.DeletedAt == null);

    // Filtrado como Product.SKU, pero por la COPIA del borrado lógico que lleva la propia
    // variante: el filtro de un índice no puede mirar la tabla del producto. Sin él, retirar
    // un producto bloqueaba para siempre su SKU a través de su variante sin talla.
    modelBuilder.Entity<ProductVariant>()
        .HasIndex(v => v.SKU)
        .IsUnique()
        .HasFilter("[DeletedAt] IS NULL");

    // Una talla por producto. EF lo filtra por `Size IS NOT NULL`, así que la variante sin
    // talla no choca consigo misma; que haya solo una la vigila ProductVariantRules.
    modelBuilder.Entity<ProductVariant>()
        .HasIndex(v => new { v.ProductId, v.Size })
        .IsUnique();

        modelBuilder.Entity<ProcessedWebhookEvent>().HasKey(e => e.Id);
    modelBuilder.Entity<ProcessedWebhookEvent>()
        .Property(e => e.Provider).HasConversion<string>().HasMaxLength(30);

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

    // La línea apunta al producto DE VERDAD, con clave foránea. Antes era un int suelto:
    // borrar un producto vendido funcionaba en silencio y la referencia quedaba colgando.
    // Restrict y no Cascade: una orden no se borra porque el catálogo cambie. Lo que hace
    // posible retirar un producto sin chocar con esto es el borrado lógico.
    modelBuilder.Entity<OrderItem>()
        .HasOne<Product>()
        .WithMany()
        .HasForeignKey(i => i.ProductId)
        .OnDelete(DeleteBehavior.Restrict);

    // Igual con la variante, y por la misma razón: por eso una talla se desactiva y no se borra.
    modelBuilder.Entity<OrderItem>()
        .HasOne<ProductVariant>()
        .WithMany()
        .HasForeignKey(i => i.VariantId)
        .OnDelete(DeleteBehavior.Restrict);
  }


  // Auditoría automática: equivale a @EnableJpaAuditing + @CreatedDate/@LastModifiedDate.
  // Quitar la llamada a StampAuditFields compila y deja de estampar en silencio.

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
