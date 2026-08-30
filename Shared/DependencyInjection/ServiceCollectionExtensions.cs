using ApiEcommerce.Data;
using ApiEcommerce.Mapping;
using ApiEcommerce.Repository;
using ApiEcommerce.Service;
using ApiEcommerce.Service.Auth;
using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Storage;

namespace ApiEcommerce.Shared.DependencyInjection;


/// <summary>
/// <b>Composition root</b>: agrupa los registros de cada feature en tres bloques por
/// capa, para que <c>Program.cs</c> se lea como un índice.
/// </summary>
/// <remarks>
/// <para>
/// La regla del proyecto es: <b>cada feature registra lo suyo, en su propia carpeta</b>
/// (<c>Data/PersistenceExtensions</c>, <c>Shared/Caching/CachingExtensions</c>,
/// <c>Service/Auth/AuthExtensions</c>…). Este archivo <b>no registra nada</b>: solo
/// compone. Antes era un único archivo de 340 líneas que conocía las siete capas a la
/// vez, y crecía con cada feature; ahora añadir una toca su carpeta y, como mucho,
/// una línea de aquí.
/// </para>
/// <para>
/// Los tres bloques declaran además la <b>dirección de las dependencias</b>:
/// Web → Infrastructure → Application. En un proyecto único eso es una convención, no
/// una frontera que imponga el compilador; pero son exactamente las costuras por
/// donde se parte la solución en proyectos (<c>ApiEcommerce.Api</c> /
/// <c>.Infrastructure</c> / <c>.Application</c>) el día que haga falta, sin reescribir
/// el registro.
/// </para>
/// <para>
/// <b>Lifetimes.</b> Todo lo que dependa de <see cref="AppDbContext"/> va
/// <c>Scoped</c>: el contexto vive lo que dura el request y capturarlo en un singleton
/// corrompería el change tracker. Lo que no guarda estado por request y solo depende
/// de singletons (<c>IJwtTokenService</c>, <c>ICacheService</c>, <c>IFileStorage</c>)
/// va <c>Singleton</c>, y va <b>igual en todas sus ramas de registro</b>.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{

  /// <summary>
  /// Capa de aplicación: mapeo, reglas de negocio, CRUD compuesto y servicios por
  /// entidad. Es lo único que hay que ampliar al añadir una entidad.
  /// </summary>
  public static IServiceCollection AddApplication(this IServiceCollection services)
      => services
          .AddObjectMapping()      // Mapping/MappingExtensions.cs
          .AddDomainServices();    // Service/ApplicationServiceExtensions.cs


  /// <summary>
  /// Infraestructura: todo lo que habla con algo de fuera del proceso —base de datos,
  /// Redis, disco— más la identidad, que se apoya en la base.
  /// </summary>
  public static IServiceCollection AddInfrastructure(
      this IServiceCollection services, IConfiguration configuration)
      => services
          .AddPersistence(configuration)        // Data/PersistenceExtensions.cs
          .AddRepositories()                    // Repository/RepositoryExtensions.cs
          .AddDistributedCaching(configuration) // Shared/Caching/CachingExtensions.cs
          .AddFileStorage(configuration)        // Shared/Storage/StorageExtensions.cs
          .AddIdentityAndJwt(configuration);    // Service/Auth/AuthExtensions.cs


  /// <summary>
  /// Superficie HTTP: controllers, versionado y documentación, CORS, límites de
  /// tasa, traducción de errores y sondas de salud.
  /// </summary>
  public static IServiceCollection AddWebApi(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddControllers();

    return services
        .AddApiVersioningAndDocs()        // Shared/Http/ApiDocumentationExtensions.cs
        .AddCorsPolicy(configuration)     // Shared/Http/CorsPolicies.cs
        .AddRateLimiting()                // Shared/Http/RateLimitPolicies.cs
        .AddErrorHandling()               // Shared/Http/ErrorHandlingExtensions.cs
        .AddHealthProbes(configuration);  // Shared/Http/HealthCheckExtensions.cs
  }

}
