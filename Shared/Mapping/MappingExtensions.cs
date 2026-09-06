using System.Reflection;

namespace ApiEcommerce.Shared.Mapping;


/// <summary>Registro en DI de AutoMapper.</summary>
public static class MappingExtensions
{
  /// <summary>
  /// Registra todos los <c>Profile</c> de los ensamblados indicados. Crear
  /// <c>Features/&lt;Contexto&gt;/Mapping/XProfile.cs</c> basta; no hay que registrarlo a mano.
  /// </summary>
  /// <remarks>
  /// Los ensamblados llegan por parámetro para que <c>Shared/</c> no tenga que nombrar un
  /// tipo de <c>Features/</c>, que sería la dependencia al revés: quien conoce ambos lados
  /// es el composition root.
  /// </remarks>
  public static IServiceCollection AddObjectMapping(
      this IServiceCollection services, params Assembly[] assemblies)
  {
    services.AddAutoMapper(cfg => { }, assemblies);
    return services;
  }
}
