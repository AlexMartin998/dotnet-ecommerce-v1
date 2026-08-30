namespace ApiEcommerce.Shared.Crud;


public static class CrudExtensions
{
  /// <summary>
  /// Los <b>genéricos abiertos</b> del CRUD compuesto. Son mecanismo transversal, no de
  /// ningún slice: por eso viven en <c>Shared/</c> y se registran una sola vez.
  /// </summary>
  /// <remarks>
  /// <c>NoEntityRules&lt;,,&gt;</c> es el "sin reglas" por defecto. Se registra como genérico
  /// <b>abierto</b> para que una entidad nueva funcione sin escribir nada; las entidades
  /// que sí tienen reglas registran su implementación <b>cerrada</b> en el
  /// <c>AddXxxFeature()</c> de su slice, y el contenedor prefiere siempre la coincidencia
  /// exacta sobre el genérico abierto, sin depender del orden de registro.
  /// </remarks>
  public static IServiceCollection AddGenericCrud(this IServiceCollection services)
  {
    services.AddScoped(typeof(IEntityRules<,,>), typeof(NoEntityRules<,,>));
    return services;
  }
}
