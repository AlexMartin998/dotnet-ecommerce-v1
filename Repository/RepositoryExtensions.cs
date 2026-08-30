namespace ApiEcommerce.Repository;


/// <summary>Registro de los repositorios: el genérico abierto + uno por entidad.</summary>
public static class RepositoryExtensions
{
  /// <remarks>
  /// Todo <b>Scoped</b>: dependen de <c>AppDbContext</c>, que vive lo que dura el
  /// request. Capturarlos en un singleton corrompería el change tracker.
  /// </remarks>
  public static IServiceCollection AddRepositories(this IServiceCollection services)
  {
    // permite inyectar IBaseRepository<X> sin escribir un repositorio específico
    services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));

    services.AddScoped<ICategoryRepository, CategoryRepository>();
    services.AddScoped<IProductRepository, ProductRepository>();

    return services;
  }
}
