using Asp.Versioning;
using Microsoft.Extensions.Options;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiEcommerce.Shared.Http;


/// <summary>Registro en DI del versionado de la API y su documentación.</summary>
public static class ApiDocumentationExtensions
{
  /// <summary>
  /// Versionado de la API por segmento de URL (<c>/api/v1/...</c>) más un documento de
  /// Swagger por versión.
  /// </summary>
  /// <remarks>
  /// El segmento de URL es el único que se ve en un log, se cachea y se comparte como
  /// enlace. <c>AssumeDefaultVersionWhenUnspecified</c> queda en <c>false</c> porque con
  /// versionado por ruta no aporta: <c>/api/category</c> ya da 404 antes del versionador.
  /// </remarks>
  public static IServiceCollection AddApiVersioningAndDocs(this IServiceCollection services)
  {
    services.AddApiVersioning(options =>
    {
      options.DefaultApiVersion = new ApiVersion(1, 0);
      options.AssumeDefaultVersionWhenUnspecified = false;

      // Emite `api-supported-versions` / `api-deprecated-versions`: el cliente se entera de
      // que su versión va a morir sin leer documentación.
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
