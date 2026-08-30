using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Models;
using ApiEcommerce.Features.Catalog.Repository;
using ApiEcommerce.Features.Catalog.Service;
using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Crud;
using ApiEcommerce.Shared.Persistence;

namespace ApiEcommerce.Features.Catalog;


/// <summary>
/// Registro del contexto acotado <b>Catálogo</b>: categorías y productos.
/// </summary>
/// <remarks>
/// <para>
/// El slice registra <b>todo lo suyo</b> —repositorios, reglas, CRUD compuesto y
/// servicios— en un solo sitio. Añadir una entidad al catálogo (una unidad de medida, una
/// etiqueta de producto, una marca) es tocar <b>esta carpeta y este archivo</b>, no siete
/// carpetas repartidas por el proyecto.
/// </para>
/// <para>
/// Y nótese que una entidad así <b>no crea un slice nuevo</b>: `UnitOfMeasurement` o
/// `ProductTag` no son contextos acotados, son parte del catálogo. Un slice por entidad
/// degenera en la misma dispersión que se venía a quitar, solo que con más carpetas.
/// </para>
/// </remarks>
public static class CatalogExtensions
{
  public static IServiceCollection AddCatalogFeature(this IServiceCollection services)
  {
    // ---- repositorios ------------------------------------------------------
    services.AddScoped<ICategoryRepository, CategoryRepository>();
    services.AddScoped<IProductRepository, ProductRepository>();

    // ---- reglas de negocio -------------------------------------------------
    // Registro CERRADO por entidad: el contenedor prefiere siempre la coincidencia exacta
    // sobre el genérico abierto NoEntityRules<,,>, así que estas ganan.
    services.AddScoped<IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>, CategoryRules>();
    services.AddScoped<IEntityRules<Product, CreateProductDto, UpdateProductDto>, ProductRules>();

    // ---- CRUD compuesto ----------------------------------------------------
    // ICrudService no lleva TEntity (para que el controller no pueda ver la entidad),
    // así que el registro es cerrado: una línea por entidad.
    services.AddScoped<
        ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>,
        CrudService<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto>>();

    services.AddScoped<
        ICrudService<ProductDto, CreateProductDto, UpdateProductDto>,
        CrudService<Product, ProductDto, CreateProductDto, UpdateProductDto>>();

    // ---- servicios ---------------------------------------------------------
    services.AddScoped<IProductService, ProductService>();

    // Category se registra DECORADO: el contenedor construye el servicio real y lo
    // envuelve en el que cachea. Quien pide ICategoryService recibe el decorador y no se
    // entera. El tipo CONCRETO se registra aparte porque, si solo estuviera la interfaz,
    // el decorador no tendría de dónde sacar el servicio interno sin recursión infinita.
    services.AddScoped<CategoryService>();
    services.AddScoped<ICategoryService>(sp => new CachedCategoryService(
        sp.GetRequiredService<CategoryService>(),
        sp.GetRequiredService<ICacheService>()));

    return services;
  }
}
