using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Genera un documento de Swagger por versión de API descubierta, en vez de un
/// <c>SwaggerDoc</c> escrito a mano por cada versión.
/// </summary>
/// <remarks>
/// Al ser un <c>IConfigureOptions&lt;SwaggerGenOptions&gt;</c>, DI lo construye con el
/// <see cref="IApiVersionDescriptionProvider"/> ya poblado: añadir una <c>v3</c> es poner
/// el atributo en un controller, sin tocar <c>Program.cs</c>.
/// </remarks>
public sealed class ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
  : IConfigureOptions<SwaggerGenOptions>
{
  /// <summary>Declara un documento por versión y la seguridad Bearer del botón Authorize.</summary>
  public void Configure(SwaggerGenOptions options)
  {
    foreach (var description in provider.ApiVersionDescriptions)
      options.SwaggerDoc(description.GroupName, DescriptionFor(description));

    // ---- botón Authorize --------------------------------------------------
    options.AddSecurityDefinition("Bearer", new OpenApiSecurityScheme
    {
      Description = "Pega aquí el token que devuelve POST /api/v1/auth/login. "
                  + "Swagger añade el prefijo 'Bearer ' por ti.",
      Name = "Authorization",
      In = ParameterLocation.Header,
      Type = SecuritySchemeType.Http,
      Scheme = "bearer",
      BearerFormat = "JWT"
    });

    options.AddSecurityRequirement(new OpenApiSecurityRequirement
    {
      {
        new OpenApiSecurityScheme
        {
          Reference = new OpenApiReference
          {
            Type = ReferenceType.SecurityScheme,
            Id = "Bearer"
          }
        },
        Array.Empty<string>()
      }
    });

    // Incluye los comentarios /// del código en la documentación. El .csproj
    // genera el XML (GenerateDocumentationFile) y el warning 1591 va suprimido.
    var xml = Path.Combine(AppContext.BaseDirectory, $"{typeof(Program).Assembly.GetName().Name}.xml");
    if (File.Exists(xml))
      options.IncludeXmlComments(xml);
  }

  private static OpenApiInfo DescriptionFor(ApiVersionDescription description)
  {
    var info = new OpenApiInfo
    {
      Title = "ApiEcommerce",
      Version = description.ApiVersion.ToString(),
      Description = "API de e-commerce en ASP.NET Core 9 con arquitectura estilo Spring Boot: "
                  + "controller → service → repository, reglas de negocio compuestas y errores RFC 7807."
    };

    // Sale del atributo [ApiVersion("x", Deprecated = true)], que además emite la cabecera
    // `api-deprecated-versions`.
    if (description.IsDeprecated)
      info.Description += " ⚠️ Esta versión está deprecada.";

    return info;
  }
}
