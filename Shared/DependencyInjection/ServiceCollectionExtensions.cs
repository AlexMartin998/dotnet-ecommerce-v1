using ApiEcommerce.Data;
using ApiEcommerce.Features.Accounts;
using ApiEcommerce.Features.Catalog;
using ApiEcommerce.Features.Catalog.Mapping;
using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Http.Health;
using ApiEcommerce.Shared.Mapping;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Shared.Observability;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Shared.Storage;

namespace ApiEcommerce.Shared.DependencyInjection;


/// <summary>
/// <b>Composition root</b>: compone en tres bloques lo que cada slice y cada pieza
/// transversal registran por su cuenta. Este archivo <b>no registra nada</b>.
/// </summary>
/// <remarks>
/// <para>
/// La organización es <b>vertical slicing por contexto acotado</b>: cada carpeta de
/// <c>Features/</c> es un contexto de dominio con <b>todo lo suyo dentro</b>
/// (<c>Models/</c>, <c>Dtos/</c>, <c>Repository/</c>, <c>Service/</c>, <c>Mapping/</c>,
/// <c>Controllers/</c>) y su propio <c>XxxExtensions.AddXxxFeature()</c>.
/// </para>
/// <para>
/// <b>Un slice es un contexto acotado, no una entidad.</b> `Category` y `Product` viven
/// juntos en <c>Catalog</c>, y ahí irían también `UnitOfMeasurement`, `ProductTag` o
/// `Brand`. Un slice por entidad reproduce la dispersión que el slicing venía a quitar,
/// solo que con más carpetas. La pregunta para decidir es de DDD: <i>¿esto tiene su propio
/// lenguaje y sus propias invariantes, o es parte del vocabulario de otro?</i>
/// </para>
/// <para>
/// Los tres bloques declaran además la dirección de las dependencias:
/// <b>Web → Features → Shared</b>. En un proyecto único es una convención, pero son las
/// costuras exactas por donde se parte la solución en proyectos el día que haga falta.
/// </para>
/// <para>
/// <b>Lifetimes.</b> Todo lo que dependa de <see cref="AppDbContext"/> va <c>Scoped</c>.
/// Lo que no guarda estado por request y solo depende de singletons va <c>Singleton</c>,
/// y va <b>igual en todas sus ramas de registro</b>.
/// </para>
/// </remarks>
public static class ServiceCollectionExtensions
{

  /// <summary>
  /// Infraestructura transversal: lo que no pertenece a ningún dominio y todos los slices
  /// usan. Vive en <c>Shared/</c>.
  /// </summary>
  public static IServiceCollection AddSharedInfrastructure(
      this IServiceCollection services, IConfiguration configuration)
      => services
          .AddPersistence(configuration)        // Shared/Persistence  — EF Core + SQL Server + ITransactionRunner
          .AddGenericCrud()                     // Shared/Crud         — genéricos abiertos del CRUD compuesto
          // El composition root es el ÚNICO sitio de Shared/ que puede nombrar tipos de
          // Features/: es literalmente su trabajo. Por eso el ensamblado a escanear se
          // pasa desde aquí y no se resuelve dentro de Shared/Mapping.
          .AddObjectMapping(typeof(CategoryProfile).Assembly)  // Shared/Mapping — AutoMapper
          .AddDistributedCaching(configuration) // Shared/Caching      — Redis + idempotencia
          .AddFileStorage(configuration)        // Shared/Storage      — almacenamiento de archivos
          .AddMessaging(configuration)          // Shared/Messaging    — outbox + RabbitMQ
          .AddObservability(configuration);     // Shared/Observability — trazas y métricas


  /// <summary>
  /// Los contextos acotados. <b>Añadir un slice = crear su carpeta y una línea aquí.</b>
  /// </summary>
  public static IServiceCollection AddFeatures(
      this IServiceCollection services, IConfiguration configuration)
      => services
          .AddAccountsFeature(configuration)    // Features/Accounts — identidad, JWT, autorización
          .AddCatalogFeature(configuration);    // Features/Catalog  — categorías y productos


  /// <summary>
  /// Superficie HTTP: controllers, versionado y documentación, CORS, límites de tasa,
  /// traducción de errores y sondas de salud.
  /// </summary>
  public static IServiceCollection AddWebApi(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddControllers();

    // HSTS. El valor de fábrica de ASP.NET Core son **30 días**, que no sirve de mucho:
    // la recomendación operativa es un año, porque la protección solo vale mientras el
    // navegador recuerde la política.
    //
    // ⚠️ SIN `Preload` ni `IncludeSubDomains` a propósito. `Preload` es una **puerta de
    // un solo sentido**: entrar en la lista de los navegadores lleva meses y salir, más;
    // e `IncludeSubDomains` rompe cualquier subdominio que aún se sirva en claro. Las dos
    // se activan cuando alguien lo decida a sabiendas, no por defecto.
    services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

    return services
        .AddApiVersioningAndDocs()        // Shared/Http/ApiDocumentationExtensions
        .AddCorsPolicy(configuration)     // Shared/Http/CorsPolicies
        .AddRateLimiting(configuration)   // Shared/Http/RateLimitPolicies
        .AddErrorHandling()               // Shared/Http/ErrorHandlingExtensions
        .AddHealthProbes(configuration);  // Shared/Http/Health/HealthCheckExtensions
  }

}
