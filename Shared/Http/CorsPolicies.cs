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
    // Se filtran los vacíos porque la configuración de .NET FUSIONA colecciones por
    // clave, no las reemplaza: definir `Cors__AllowedOrigins__0` por entorno NO borra
    // los índices 1, 2... de appsettings.json. Con la lista base vacía y este filtro,
    // cada entorno declara sus orígenes y no hereda los de desarrollo sin querer.
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
              // ⚠️ Imprescindible desde que el refresh token viaja en cookie: sin esto el
              // navegador NO la manda en una petición a otro origen, y tampoco aceptaría
              // la respuesta que la establece. El login funcionaría y el refresh no,
              // otra vez sin ningún error en el servidor.
              //
              // Es legal aquí precisamente porque arriba hay una lista explícita de
              // orígenes: combinar credenciales con `AllowAnyOrigin()` está PROHIBIDO por
              // la especificación y ASP.NET Core lanza en tiempo de ejecución si se
              // intenta. Es la mejor razón para no haber puesto nunca el comodín.
              .AllowCredentials()
              // Deja que el front lea las cabeceras de versión que emite ReportApiVersions.
              .WithExposedHeaders("api-supported-versions", "api-deprecated-versions");
      });
    });

    return services;
  }
}
