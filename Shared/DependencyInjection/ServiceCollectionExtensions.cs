using ApiEcommerce.Data;
using ApiEcommerce.Features.Accounts;
using ApiEcommerce.Features.Catalog;
using ApiEcommerce.Features.Catalog.Mapping;
using ApiEcommerce.Features.Ordering;
using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Shared.Documents;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Http.Health;
using ApiEcommerce.Shared.Mapping;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Shared.Observability;
using ApiEcommerce.Shared.Persistence;
using ApiEcommerce.Shared.Storage;

namespace ApiEcommerce.Shared.DependencyInjection;


/// <summary>
/// Composition root: compone en tres bloques lo que cada slice y cada pieza transversal
/// registran por su cuenta.
/// </summary>
/// <remarks>
/// No registra servicios propios —cada uno vive en el <c>XxxExtensions.cs</c> de su
/// carpeta—, salvo <c>AddControllers()</c> y <c>AddHsts()</c>, que no tienen otra. Los tres
/// bloques declaran la dirección de dependencias Web → Features → Shared.
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
          // El composition root es el único sitio de Shared/ que puede nombrar tipos de
          // Features/, así que el ensamblado a escanear se pasa desde aquí.
          .AddObjectMapping(typeof(CategoryProfile).Assembly)  // Shared/Mapping — AutoMapper
          .AddDistributedCaching(configuration) // Shared/Caching      — Redis + idempotencia
          .AddFileStorage(configuration)        // Shared/Storage      — imagenes PUBLICAS (wwwroot)
          // Otro almacén, no una duplicación: este guarda documentos privados fuera de
          // wwwroot (ver IDocumentStore).
          .AddDocumentStorage(configuration)    // Shared/Documents    — documentos PRIVADOS
          .AddMessaging(configuration)          // Shared/Messaging    — outbox + RabbitMQ
          .AddObservability(configuration);     // Shared/Observability — trazas y métricas


  /// <summary>
  /// Los contextos acotados. Añadir un slice = crear su carpeta y una línea aquí.
  /// </summary>
  /// <remarks>
  /// Un slice es un contexto acotado, no una entidad: <c>Category</c> y <c>Product</c>
  /// viven juntos en <c>Catalog</c>. La pregunta para separar es si algo tiene su propio
  /// lenguaje y sus propias invariantes o es vocabulario de otro.
  /// </remarks>
  public static IServiceCollection AddFeatures(
      this IServiceCollection services, IConfiguration configuration)
      => services
          .AddAccountsFeature(configuration)    // Features/Accounts — identidad, JWT, autorización
          .AddCatalogFeature(configuration)     // Features/Catalog  — categorías y productos
          .AddOrderingFeature(configuration);   // Features/Ordering — órdenes y comprobantes


  /// <summary>
  /// Superficie HTTP: controllers, versionado y documentación, CORS, límites de tasa,
  /// traducción de errores y sondas de salud.
  /// </summary>
  public static IServiceCollection AddWebApi(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddControllers();

    // Un año, y no los 30 días de fábrica: la protección solo vale mientras el navegador
    // recuerde la política. Sin `Preload` (es una puerta de un solo sentido) ni
    // `IncludeSubDomains` (rompe subdominios servidos en claro).
    services.AddHsts(options => options.MaxAge = TimeSpan.FromDays(365));

    return services
        .AddApiVersioningAndDocs()        // Shared/Http/ApiDocumentationExtensions
        .AddCorsPolicy(configuration)     // Shared/Http/CorsPolicies
        .AddRateLimiting(configuration)   // Shared/Http/RateLimitPolicies
        .AddErrorHandling()               // Shared/Http/ErrorHandlingExtensions
        .AddHealthProbes(configuration);  // Shared/Http/Health/HealthCheckExtensions
  }

}
