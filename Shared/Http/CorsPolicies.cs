namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Nombre de la política de CORS y su registro, juntos para no repetir el literal entre el
/// registro y el <c>UseCors</c>.
/// </summary>
public static class CorsPolicies
{
  /// <summary>Nombre de la política por defecto.</summary>
  public const string Default = "DefaultCorsPolicy";

  /// <summary>
  /// CORS con orígenes leídos de configuración (<c>Cors:AllowedOrigins</c>).
  /// </summary>
  /// <remarks>
  /// Nunca <c>AllowAnyOrigin()</c>: con un token en <c>Authorization</c>, el comodín deja
  /// que cualquier página llame a la API desde el navegador de un usuario logueado. Con la
  /// lista vacía no se permite ningún origen: falla cerrado.
  /// </remarks>
  public static IServiceCollection AddCorsPolicy(
      this IServiceCollection services, IConfiguration configuration)
  {
    // Se filtran los vacíos porque la configuración de .NET fusiona colecciones por clave:
    // definir `Cors__AllowedOrigins__0` por entorno no borra los índices de appsettings.json.
    var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>()?
        .Where(o => !string.IsNullOrWhiteSpace(o))
        .ToArray() ?? [];

    services.AddCors(options =>
    {
      options.AddPolicy(Default, policy =>
      {
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              // Imprescindible desde que el refresh token viaja en cookie: sin esto el
              // navegador no la manda entre orígenes y el refresh falla sin error en el
              // servidor. Es legal porque arriba hay una lista explícita de orígenes;
              // combinarlo con `AllowAnyOrigin()` lo prohíbe la especificación.
              .AllowCredentials()
              // Deja que el front lea las cabeceras de versión que emite ReportApiVersions.
              .WithExposedHeaders("api-supported-versions", "api-deprecated-versions");
      });
    });

    return services;
  }
}
