namespace ApiEcommerce.Mapping;


public static class MappingExtensions
{
  /// <summary>
  /// AutoMapper: registra todos los <c>Profile</c> del assembly donde vive
  /// <see cref="CategoryProfile"/>. Crear <c>Mapping/XProfile.cs</c> basta;
  /// no hay que registrarlo a mano.
  /// </summary>
  public static IServiceCollection AddObjectMapping(this IServiceCollection services)
  {
    services.AddAutoMapper(cfg => { }, typeof(CategoryProfile).Assembly);
    return services;
  }
}
