using Asp.Versioning;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiEcommerce.Shared.Http;


public static class ApiDocumentationExtensions
{
  /// <summary>
  /// Versionado de la API por <b>segmento de URL</b> (<c>/api/v1/...</c>) + un documento
  /// de Swagger por versión.
  /// </summary>
  /// <remarks>
  /// Se elige segmento de URL y no query string ni cabecera porque es el único que
  /// se ve en un log, se puede cachear y se puede compartir como enlace.
  /// Nótese que <c>AssumeDefaultVersionWhenUnspecified</c> queda en <c>false</c>: con
  /// versionado por ruta esa opción es humo — <c>/api/category</c> no matchea ninguna
  /// plantilla y da 404 antes de que el versionador llegue a opinar.
  /// </remarks>
  public static IServiceCollection AddApiVersioningAndDocs(this IServiceCollection services)
  {
    services.AddApiVersioning(options =>
    {
      options.DefaultApiVersion = new ApiVersion(1, 0);
      options.AssumeDefaultVersionWhenUnspecified = false;

      // Emite las cabeceras `api-supported-versions` / `api-deprecated-versions`:
      // el cliente se entera de que su versión va a morir sin leer documentación.
      options.ReportApiVersions = true;

      options.ApiVersionReader = new UrlSegmentApiVersionReader();
    })
    .AddApiExplorer(options =>
    {
      options.GroupNameFormat = "'v'VVV";       // v1, v1.1, v2...
      options.SubstituteApiVersionInUrl = true; // en Swagger sale /api/v1/... y no /api/v{version}/...
    });

    services.AddEndpointsApiExplorer();

    // Un documento por versión, generado desde el ApiExplorer (ver ConfigureSwaggerOptions).
    services.AddTransient<IConfigureOptions<SwaggerGenOptions>, ConfigureSwaggerOptions>();
    services.AddSwaggerGen();

    return services;
  }
}
