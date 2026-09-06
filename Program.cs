using ApiEcommerce.Shared.DependencyInjection;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Observability;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;


var builder = WebApplication.CreateBuilder(args);


// Logging estructurado: mismo ILogger<T>, pero la salida lleva propiedades tipadas.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    // Plantilla propia: la de fábrica no renderiza las propiedades del LogContext.
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}/{TraceId}] {Message:lj}{NewLine}{Exception}"));

// El sink se declara aquí y no en `Serilog:WriteTo`: declararlo en los dos suma, no sustituye.


// Cabeceras del proxy: sin esto el rate limiter particiona por la IP del proxy y los
// logs registran esa misma IP para todo el mundo.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Vaciarlas acepta las cabeceras de cualquier origen: solo vale si la API no es
    // alcanzable sin pasar por el proxy. Declarar aquí su red en cuanto se conozca.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services
    .AddSharedInfrastructure(builder.Configuration)  // Shared/  — EF Core, Redis, disco, mensajería
    .AddFeatures(builder.Configuration)              // Features/ — un bloque por contexto acotado
    .AddWebApi(builder.Configuration);               // superficie HTTP

var app = builder.Build();


// Scope propio: el contenedor raíz no puede resolver servicios Scoped.
using (var scope = app.Services.CreateScope())
{
    // Migrar al arrancar: la imagen de runtime no lleva SDK ni dotnet-ef, y el health
    // check solo comprueba la conexión, no el esquema.
    var db = scope.ServiceProvider.GetRequiredService<ApiEcommerce.Data.AppDbContext>();
    await db.Database.MigrateAsync();

    await ApiEcommerce.Data.DataSeeder.SeedAsync(scope.ServiceProvider);
}


// Pipeline HTTP. Cada middleware solo ve lo que ocurre después de él.

// Primero: reescribe IP y esquema desde las cabeceras del proxy, para todo lo que sigue.
app.UseForwardedHeaders();

// Antes de todo lo demás: así hasta los fallos tempranos llevan CorrelationId.
app.UseMiddleware<CorrelationIdMiddleware>();

// Una línea por request con método, ruta, código y duración.
app.UseSerilogRequestLogging();

// Lo más arriba posible: solo captura lo que ocurre después de él.
app.UseExceptionHandler();

// Justo debajo del anterior: el diagnóstico del framework escribe su "unhandled
// exception" a nivel Error antes de llamar a ningún IExceptionHandler, así que las
// excepciones de cliente colgado hay que absorberlas antes de que le lleguen.
app.UseMiddleware<ClientAbortMiddleware>();

// Convierte los 401/403/404 vacíos del framework en ProblemDetails.
app.UseStatusCodePages();

if (app.Environment.IsDevelopment())
{
    // swagger only in development -----
    app.UseSwagger();
    app.UseSwaggerUI(options =>
    {
        // Un endpoint por versión descubierta: añadir una v2 no toca este archivo.
        var provider = app.Services.GetRequiredService<IApiVersionDescriptionProvider>();

        foreach (var description in provider.ApiVersionDescriptions.OrderByDescending(d => d.ApiVersion))
            options.SwaggerEndpoint(
                $"/swagger/{description.GroupName}/swagger.json",
                description.GroupName.ToUpperInvariant());
    });
}

// HSTS evita la primera petición en claro, cosa que UseHttpsRedirection ya no puede.
// Fuera de Development a propósito: la cabecera queda cacheada para `localhost` y rompe
// cualquier otro proyecto servido en claro en ese host.
if (!app.Environment.IsDevelopment())
    app.UseHsts();   // max-age configurado en AddWebApi; el de fábrica son 30 días

app.UseHttpsRedirection();

// Antes de la autenticación: un preflight OPTIONS no lleva token y debe responderse igual.
app.UseCors(CorsPolicies.Default);

// Antes de auth: rechazar una avalancha es más barato que validar su token.
app.UseRateLimiter();

// wwwroot/ (imágenes de producto). Es terminal para lo que sirve, así que más arriba se
// saltaría el limitador y no recibiría cabeceras CORS; antes de auth porque son públicas.
app.UseStaticFiles();

// Primero quién eres, después qué puedes hacer.
app.UseAuthentication();
app.UseAuthorization();

// endpoints ----
app.MapControllers();

// Readiness: 200 solo si SQL Server y Redis responden. La sonda de vida (`/health`, en
// HealthController) no toca dependencias externas para no provocar reinicios en cascada.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    // Sin el predicado los tags serían decorativos y un check futuro sin tag entraría solo.
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();


/// <summary>
/// Punto de entrada. Se declara <c>public partial</c> porque la clase generada por las
/// instrucciones de nivel superior es <c>internal</c> y
/// <c>WebApplicationFactory&lt;Program&gt;</c> no la vería.
/// </summary>
public partial class Program;
