using System.Reflection;

namespace ApiEcommerce.Shared.Mapping;


public static class MappingExtensions
{
  /// <summary>
  /// AutoMapper: registra todos los <c>Profile</c> de los ensamblados indicados. Crear
  /// <c>Features/&lt;Contexto&gt;/Mapping/XProfile.cs</c> basta; no hay que registrarlo a mano.
  /// </summary>
  /// <remarks>
  /// Los ensamblados se reciben por parámetro en vez de resolverse aquí con
  /// <c>typeof(CategoryProfile).Assembly</c>. Aquello obligaba a <c>Shared/</c> a nombrar
  /// un tipo de <c>Features/</c>, que es la dirección de dependencia al revés. Quien sí
  /// puede conocer ambos lados es el <b>composition root</b>, y es quien los pasa.
  /// </remarks>
  public static IServiceCollection AddObjectMapping(
      this IServiceCollection services, params Assembly[] assemblies)
  {
    services.AddAutoMapper(cfg => { }, assemblies);
    return services;
  }
}
