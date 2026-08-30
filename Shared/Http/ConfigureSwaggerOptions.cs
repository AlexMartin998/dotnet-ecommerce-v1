using Asp.Versioning.ApiExplorer;
using Microsoft.Extensions.Options;
using Microsoft.OpenApi.Models;
using Swashbuckle.AspNetCore.SwaggerGen;

namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Genera <b>un documento de Swagger por versión de API descubierta</b>, en vez de
/// escribir un <c>SwaggerDoc("v1")</c> a mano por cada versión.
/// </summary>
/// <remarks>
/// <para>
/// El patrón es <c>IConfigureOptions&lt;SwaggerGenOptions&gt;</c>: DI construye esta clase
/// con el <see cref="IApiVersionDescriptionProvider"/> ya poblado por
/// <c>AddApiExplorer</c>, así que añadir una <c>v3</c> es poner <c>[ApiVersion("3.0")]</c>
/// en un controller — <b>cero cambios en <c>Program.cs</c></b>.
/// </para>
/// <para>
/// Aquí también vive la definición de seguridad Bearer, que es lo que pinta el botón
/// <b>Authorize</b> de la UI de Swagger.
/// </para>
/// </remarks>
public sealed class ConfigureSwaggerOptions(IApiVersionDescriptionProvider provider)
  : IConfigureOptions<SwaggerGenOptions>
{
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

    // Marcado real de deprecación: sale del atributo [ApiVersion("x", Deprecated = true)],
    // que además hace que la respuesta lleve la cabecera `api-deprecated-versions`.
    if (description.IsDeprecated)
      info.Description += " ⚠️ Esta versión está deprecada.";

    return info;
  }
}
