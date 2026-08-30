using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Service.Crud;
using ApiEcommerce.Shared.Caching;

namespace ApiEcommerce.Service;


/// <summary>
/// Registro de la capa de servicio: reglas de negocio, CRUD compuesto y servicios
/// por entidad.
/// </summary>
/// <remarks>
/// Este es el único bloque que hay que tocar al añadir una entidad nueva, y el paso
/// que más se olvida del slice vertical (ver <c>AGENTS/docs/00-arquitectura.md</c>).
/// </remarks>
public static class ApplicationServiceExtensions
{
  public static IServiceCollection AddDomainServices(this IServiceCollection services)
  {
    // ---- reglas de negocio ------------------------------------------------
    // Por defecto, "sin reglas". Se registra como genérico abierto para que una
    // entidad nueva funcione sin escribir nada.
    services.AddScoped(typeof(IEntityRules<,,>), typeof(NoEntityRules<,,>));

    // Registro CERRADO por entidad: el contenedor prefiere siempre la coincidencia
    // exacta sobre el genérico abierto, así que estas ganan sobre NoEntityRules.
    services.AddScoped<IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>, CategoryRules>();
    services.AddScoped<IEntityRules<Product, CreateProductDto, UpdateProductDto>, ProductRules>();

    // ---- CRUD compuesto ---------------------------------------------------
    // ICrudService no lleva TEntity (para que el controller no pueda ver la entidad),
    // así que el registro es cerrado: una línea por entidad.
    services.AddScoped<
        ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>,
        CrudService<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto>>();

    services.AddScoped<
        ICrudService<ProductDto, CreateProductDto, UpdateProductDto>,
        CrudService<Product, ProductDto, CreateProductDto, UpdateProductDto>>();

    // ---- servicios por entidad -------------------------------------------
    services.AddScoped<IProductService, ProductService>();

    // Category se registra DECORADO: el contenedor construye el servicio real y lo
    // envuelve en el que cachea. Quien pide ICategoryService recibe el decorador y
    // no se entera. (Con Scrutor sería services.Decorate<...>(); a mano son 3 líneas
    // y no se añade una dependencia por eso.)
    //
    // El tipo CONCRETO se registra aparte para que el contenedor pueda construirlo:
    // si solo estuviera registrada la interfaz, `new CachedCategoryService(inner)`
    // no tendría de dónde sacar `inner` sin recursión infinita.
    services.AddScoped<CategoryService>();
    services.AddScoped<ICategoryService>(sp => new CachedCategoryService(
        sp.GetRequiredService<CategoryService>(),
        sp.GetRequiredService<ICacheService>()));

    return services;
  }
}
