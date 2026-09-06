using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Features.Catalog.Service;
using ApiEcommerce.Features.Catalog.Messaging;
using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Shared.Messaging;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Features.Catalog;


/// <summary>
/// Registro del contexto acotado <b>Catálogo</b>: categorías y productos.
/// </summary>
/// <remarks>
/// El slice registra aquí todo lo suyo. Una entidad más del catálogo (una marca, una
/// etiqueta) entra en este archivo; no es un contexto acotado nuevo.
/// </remarks>
public static class CatalogExtensions
{
  /// <summary>Registra repositorios, reglas, servicios y consumidores del catálogo.</summary>
  public static IServiceCollection AddCatalogFeature(
      this IServiceCollection services, IConfiguration configuration)
  {
    var rabbit = configuration.GetSection(RabbitMqOptions.SectionName).Get<RabbitMqOptions>()
                 ?? new RabbitMqOptions();

    // ---- repositorios ------------------------------------------------------
    services.AddScoped<ICategoryRepository, CategoryRepository>();
    services.AddScoped<IProductRepository, ProductRepository>();

    // ---- reglas de negocio -------------------------------------------------
    // Registro cerrado por entidad: gana sobre el genérico abierto NoEntityRules.
    services.AddScoped<IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>, CategoryRules>();
    services.AddScoped<IEntityRules<Product, CreateProductDto, UpdateProductDto>, ProductRules>();

    // ---- CRUD compuesto ----------------------------------------------------
    // ICrudService no lleva TEntity, así que el registro es cerrado: una línea por entidad.
    services.AddScoped<
        ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>,
        CrudService<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto>>();

    services.AddScoped<
        ICrudService<ProductDto, CreateProductDto, UpdateProductDto>,
        CrudService<Product, ProductDto, CreateProductDto, UpdateProductDto>>();

    // ---- servicios ---------------------------------------------------------
    services.AddScoped<IProductService, ProductService>();

    // Category se registra decorado: quien pide ICategoryService recibe el que cachea. El
    // tipo concreto se registra aparte o el decorador no podría resolver su interior.
    services.AddScoped<CategoryService>();
    services.AddScoped<ICategoryService>(sp => new CachedCategoryService(
        sp.GetRequiredService<CategoryService>(),
        sp.GetRequiredService<ICacheService>()));

    // ---- consumidores de eventos -------------------------------------------
    // Quién reacciona a un evento del catálogo es asunto del catálogo; Shared/Messaging
    // solo pone el mecanismo. El efecto se registra siempre, también sin broker, para
    // poder probarlo sin AMQP delante.
    services.AddScoped<IProductPurchasedHandler, LowStockNotifier>();

    // La dead-letter es la heredada (`{Exchange}.dlx`) y no una por cola como en los
    // slices nuevos: la cola ya existe en los brokers así, y redeclararla con otro
    // x-dead-letter-exchange da 406 PRECONDITION_FAILED y tumba la mensajería.
    services.AddEventConsumer<ProductPurchasedConsumer>(
        configuration,
        new EventSubscription(rabbit.Queue, rabbit.RoutingKey, rabbit.DeadLetterExchange));

    return services;
  }
}
