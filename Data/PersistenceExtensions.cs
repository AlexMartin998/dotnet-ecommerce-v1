using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Db;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Data;


/// <summary>
/// Registro de la capa de persistencia. Vive <b>junto al <see cref="AppDbContext"/></b>
/// que registra: añadir algo a esta capa toca una sola carpeta.
/// </summary>
public static class PersistenceExtensions
{
  /// <summary>EF Core + SQL Server, con reintentos ante fallos transitorios.</summary>
  /// <remarks>
  /// <para>
  /// <b><c>EnableRetryOnFailure</c> no es opcional.</b> Sin él,
  /// <c>Database.CreateExecutionStrategy()</c> devuelve una estrategia
  /// <i>no reintentante</i>, y eso deja sin efecto el
  /// <c>Shared/Db/TransactionalAttribute</c>, que está escrito precisamente para
  /// sobrevivir a un corte transitorio. Contra un SQL Server en contenedor o
  /// gestionado, cada micro-corte de red se convertía en un 500.
  /// </para>
  /// <para>
  /// Contrapartida a conocer: con una estrategia reintentante, EF <b>prohíbe</b>
  /// abrir una transacción a mano fuera de <c>strategy.ExecuteAsync(...)</c>.
  /// El <c>[Transactional]</c> ya lo hace así; cualquier transacción nueva también debe.
  /// </para>
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

    // Los datos de arranque son de esta capa: DataSeeder vive aquí al lado.
    //
    // La validación es CONDICIONAL: solo se exige contraseña de admin si el seeding
    // está encendido. Con `[Required]` en la propiedad, un despliegue con
    // `Seed__Enabled=false` (lo normal en producción, sin contraseña definida) moría
    // al arrancar en bucle, porque leer .Value valida antes de que nadie pueda mirar
    // el flag.
    services.AddOptions<SeedOptions>()
        .Bind(configuration.GetSection(SeedOptions.SectionName))
        .ValidateDataAnnotations()
        .Validate(o => !o.Enabled
                       || (!string.IsNullOrWhiteSpace(o.AdminPassword) && o.AdminPassword.Length >= 8),
                  "Seed:AdminPassword is required (min 8 chars) when Seed:Enabled is true")
        .ValidateOnStart();

    // Scoped: comparte el AppDbContext del request, que es lo que hace que la
    // transacción cubra al repositorio y al outbox a la vez.
    services.AddScoped<ITransactionRunner, TransactionRunner>();

    return services;
  }
}
