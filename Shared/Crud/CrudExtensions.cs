namespace ApiEcommerce.Shared.Crud;


/// <summary>Registro en DI de los genéricos abiertos del CRUD compuesto.</summary>
public static class CrudExtensions
{
  /// <summary>
  /// Registra los genéricos abiertos del CRUD, que son mecanismo transversal y no de
  /// ningún slice.
  /// </summary>
  /// <remarks>
  /// <c>NoEntityRules&lt;,,&gt;</c> es el «sin reglas» por defecto; una entidad con reglas
  /// registra su implementación cerrada en su slice y el contenedor prefiere la
  /// coincidencia exacta, sin depender del orden de registro.
  /// </remarks>
  public static IServiceCollection AddGenericCrud(this IServiceCollection services)
  {
    services.AddScoped(typeof(IEntityRules<,,>), typeof(NoEntityRules<,,>));
    return services;
  }
}
