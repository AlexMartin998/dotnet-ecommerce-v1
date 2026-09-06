using ApiEcommerce.Data;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Db;
using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Shared.Idempotency;

namespace ApiEcommerce.Shared.Persistence;


/// <summary>
/// Registro de la capa de persistencia. Vive junto al <see cref="AppDbContext"/> que
/// registra, para que añadir algo a esta capa toque una sola carpeta.
/// </summary>
public static class PersistenceExtensions
{
  /// <summary>EF Core + SQL Server, con reintentos ante fallos transitorios.</summary>
  /// <remarks>
  /// <c>EnableRetryOnFailure</c> no es opcional: sin él, <c>CreateExecutionStrategy()</c>
  /// devuelve una estrategia no reintentante y <c>[Transactional]</c> queda sin efecto. A
  /// cambio, EF prohíbe abrir transacciones fuera de <c>strategy.ExecuteAsync(...)</c>.
  /// </remarks>
  public static IServiceCollection AddPersistence(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(
            configuration.GetConnectionString("ConexionSql"),
            sql => sql.EnableRetryOnFailure(
                maxRetryCount: 3,
                maxRetryDelay: TimeSpan.FromSeconds(5),
                errorNumbersToAdd: null)));

    // La validación del seeding es condicional: con `[Required]` en la propiedad, un
    // despliegue con `Seed__Enabled=false` moría al arrancar en bucle.
    services.AddOptions<SeedOptions>()
        .Bind(configuration.GetSection(SeedOptions.SectionName))
        .ValidateDataAnnotations()
        .Validate(o => !o.Enabled
                       || (!string.IsNullOrWhiteSpace(o.AdminPassword) && o.AdminPassword.Length >= 8),
                  "Seed:AdminPassword is required (min 8 chars) when Seed:Enabled is true")
        .Validate(o => !o.Enabled
                       || (!string.IsNullOrWhiteSpace(o.AdminEmail) && o.AdminEmail.Contains('@')),
                  "Seed:AdminEmail must be a valid address when Seed:Enabled is true")
        .ValidateOnStart();

    // Genérico abierto: permite inyectar IBaseRepository<X> sin escribir un repositorio
    // específico. Los repositorios propios de cada slice se registran en su AddXxxFeature().
    services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));

    // Scoped: comparte el AppDbContext del request, que es lo que hace que la
    // transacción cubra al repositorio y al outbox a la vez.
    services.AddScoped<ITransactionRunner, TransactionRunner>();

    // Aquí y no en AddDistributedCaching: es la garantía de idempotencia y vive en la
    // base, así que no se apaga cuando no hay Redis.
    services.AddScoped<ICommandLog, CommandLog>();

    return services;
  }
}
