using ApiEcommerce.Data;
using ApiEcommerce.Mapping;
using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Repository;
using ApiEcommerce.Service;
using ApiEcommerce.Service.Crud;
using ApiEcommerce.Shared.Http;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Shared.DependencyInjection;


/// <summary>
/// Registro de dependencias agrupado por capa, para que <c>Program.cs</c> se lea
/// como un índice y no crezca a 200 líneas.
/// Todo lo que dependa de <see cref="AppDbContext"/> va con lifetime <b>Scoped</b>:
/// el DbContext vive lo que dura el request y capturarlo en un singleton corrompería
/// el change tracker.
/// </summary>
public static class ServiceCollectionExtensions
{

  /// <summary>EF Core + SQL Server.</summary>
  public static IServiceCollection AddPersistence(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(configuration.GetConnectionString("ConexionSql")));

    return services;
  }

  /// <summary>AutoMapper: registra todos los Profile del assembly donde vive <see cref="CategoryProfile"/>.</summary>
  public static IServiceCollection AddObjectMapping(this IServiceCollection services)
  {
    services.AddAutoMapper(cfg => { }, typeof(CategoryProfile).Assembly);
    return services;
  }

  /// <summary>Repositorios: el genérico abierto + uno por entidad.</summary>
  public static IServiceCollection AddRepositories(this IServiceCollection services)
  {
    // permite inyectar IBaseRepository<X> sin escribir un repositorio específico
    services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));

    services.AddScoped<ICategoryRepository, CategoryRepository>();
    services.AddScoped<IProductRepository, ProductRepository>();

    return services;
  }

  /// <summary>Servicios de aplicación: CRUD compuesto, reglas por entidad y servicios por entidad.</summary>
  public static IServiceCollection AddApplicationServices(this IServiceCollection services)
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
    services.AddScoped<ICategoryService, CategoryService>();
    services.AddScoped<IProductService, ProductService>();

    return services;
  }

  /// <summary>
  /// Handler global de errores + ProblemDetails (RFC 7807).
  /// Equivalente a <c>@ControllerAdvice</c> de Spring.
  /// </summary>
  public static IServiceCollection AddErrorHandling(this IServiceCollection services)
  {
    services.AddProblemDetails();
    services.AddExceptionHandler<GlobalExceptionHandler>();

    return services;
  }

}
