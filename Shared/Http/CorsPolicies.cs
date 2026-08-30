namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Nombre de la política de CORS y su registro. Van juntos a propósito: un literal
/// repetido entre el registro y el <c>UseCors</c> es un typo esperando.
/// </summary>
public static class CorsPolicies
{
  public const string Default = "DefaultCorsPolicy";

  /// <summary>
  /// CORS con orígenes <b>leídos de configuración</b> (<c>Cors:AllowedOrigins</c>).
  /// </summary>
  /// <remarks>
  /// Nunca <c>AllowAnyOrigin()</c>: esta API viaja con un token en la cabecera
  /// <c>Authorization</c>, así que un comodín deja que cualquier página del mundo la
  /// llame desde el navegador de un usuario logueado. Si la lista viene vacía, la
  /// política no permite ningún origen — falla cerrado, no abierto.
  /// </remarks>
  public static IServiceCollection AddCorsPolicy(
      this IServiceCollection services, IConfiguration configuration)
  {
    var origins = configuration.GetSection("Cors:AllowedOrigins").Get<string[]>() ?? [];

    services.AddCors(options =>
    {
      options.AddPolicy(Default, policy =>
      {
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              // Deja que el front lea las cabeceras de versión que emite ReportApiVersions.
              .WithExposedHeaders("api-supported-versions", "api-deprecated-versions");
      });
    });

    return services;
  }
}
