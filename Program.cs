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
    // Aquí y no en `Serilog:WriteTo`: declarar el sink en los dos sitios suma, no sustituye.
    .WriteTo.Console(outputTemplate:
        "[{Timestamp:HH:mm:ss} {Level:u3}] [{CorrelationId}/{TraceId}] {Message:lj}{NewLine}{Exception}"));


// Cabeceras del proxy: sin esto el rate limiter particiona por la IP del proxy y los
// logs registran esa misma IP para todo el mundo.
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Solo se confia en los proxies declarados. Sin declararlos se conserva el valor de
    // fabrica -solo loopback-, asi que una X-Forwarded-For de fuera se IGNORA.
    //
    // Aceptarla de cualquiera es un bypass del rate limiter, no un detalle de despliegue:
    // el limitador particiona por RemoteIpAddress, que para entonces ya viene reescrita
    // por la cabecera. Medido con AuthPermitLimit=5: sin cabecera, la sexta peticion daba
    // 429; rotando X-Forwarded-For, ninguna. Fuerza bruta sin limite.
    foreach (var proxy in builder.Configuration.GetSection("ForwardedHeaders:KnownProxies")
                                               .Get<string[]>() ?? [])
        if (System.Net.IPAddress.TryParse(proxy, out var address))
            options.KnownProxies.Add(address);

    foreach (var network in builder.Configuration.GetSection("ForwardedHeaders:KnownNetworks")
                                                 .Get<string[]>() ?? [])
    {
        var parts = network.Split('/');

        if (parts.Length == 2
            && System.Net.IPAddress.TryParse(parts[0], out var prefix)
            && int.TryParse(parts[1], out var length))
            options.KnownNetworks.Add(new IPNetwork(prefix, length));
    }
});

// Valida el grafo en TODOS los entornos, no solo en Development, que es donde lo activa
// CreateBuilder por su cuenta. Los tests corren en "Testing" y son justo los que recorren
// las ramas sin Redis y sin broker: sin esto, una captive dependency o un servicio sin
// registrar en una rama fria no lo ve ni la CI ni el arranque, y sale como 500 en runtime.
builder.Host.UseDefaultServiceProvider(options =>
{
    options.ValidateScopes = true;
    options.ValidateOnBuild = true;
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
