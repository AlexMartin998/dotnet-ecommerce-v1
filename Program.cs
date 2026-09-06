using ApiEcommerce.Shared.DependencyInjection;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Observability;
using Asp.Versioning.ApiExplorer;
using Microsoft.AspNetCore.Diagnostics.HealthChecks;
using Microsoft.AspNetCore.HttpOverrides;
using Microsoft.EntityFrameworkCore;
using Serilog;


var builder = WebApplication.CreateBuilder(args);


// // // Logging estructurado --------------------------------------------
// Serilog sustituye al logger por defecto: mismo ILogger<T> en el código, pero la
// salida lleva propiedades tipadas ({Username}, {Path}...) en vez de una frase ya
// interpolada, que es lo que hace un log consultable.
builder.Host.UseSerilog((context, configuration) => configuration
    .ReadFrom.Configuration(context.Configuration)
    .Enrich.FromLogContext()
    .WriteTo.Console());


// // // Add SERVICES to the container ---------------------------------
// Tres bloques. Cada uno solo COMPONE lo que cada slice y cada pieza transversal
// registran en SU propia carpeta (ver Shared/DependencyInjection/ServiceCollectionExtensions.cs).
// Detrás de un proxy, sin esto: el rate limiter particiona por la IP DEL PROXY (o
// sea, un solo cubo de 100 req/min para todo internet), los logs registran esa misma
// IP para todo el mundo, y UseHttpsRedirection no sabe si la petición original era
// HTTPS (hoy avisa con "Failed to determine the https port" y es un no-op).
builder.Services.Configure<ForwardedHeadersOptions>(options =>
{
    options.ForwardedHeaders = ForwardedHeaders.XForwardedFor | ForwardedHeaders.XForwardedProto;

    // Vaciarlas acepta las cabeceras de CUALQUIER origen. Solo es admisible si la API
    // no es alcanzable directamente desde fuera del proxy; si lo fuera, cualquiera
    // podría falsear su IP y saltarse el rate limit. Declara aquí la red del proxy en
    // cuanto la conozcas.
    options.KnownNetworks.Clear();
    options.KnownProxies.Clear();
});

builder.Services
    .AddSharedInfrastructure(builder.Configuration)  // Shared/  — EF Core, Redis, disco, mensajería
    .AddFeatures(builder.Configuration)              // Features/ — un bloque por contexto acotado
    .AddWebApi(builder.Configuration);               // superficie HTTP

var app = builder.Build();


// // // Migraciones y datos de arranque ----------------------------------
// Scope propio: el contenedor RAÍZ no puede resolver servicios Scoped
// (AppDbContext, UserManager) y lanzaría en el arranque.
using (var scope = app.Services.CreateScope())
{
    // Sin esto, la imagen de runtime (que no lleva SDK ni dotnet-ef) arranca contra
    // una base vacía: /health responde 200, /health/ready responde Healthy —porque
    // AddDbContextCheck solo comprueba que se puede CONECTAR, no el esquema— y todos
    // los endpoints devuelven 500 "Invalid object name 'Categories'".
    // MigrateAsync además reintenta gracias a EnableRetryOnFailure, que es justo lo
    // que hace falta cuando SQL Server todavía está arrancando.
    var db = scope.ServiceProvider.GetRequiredService<ApiEcommerce.Data.AppDbContext>();
    await db.Database.MigrateAsync();

    await ApiEcommerce.Data.DataSeeder.SeedAsync(scope.ServiceProvider);
}


// // // Configure the HTTP request pipeline ------------------------------
// El orden del pipeline NO es decorativo: cada middleware solo ve lo que ocurre
// después de él. De arriba abajo: errores → swagger → https → cors → auth → endpoints.

// Lo PRIMERO del pipeline: reescribe la IP y el esquema a partir de las cabeceras del
// proxy, para que todo lo que viene después (logs, rate limit, redirección) vea los
// datos reales del cliente y no los del proxy.
app.UseForwardedHeaders();

// Justo después de resolver la IP real y ANTES de todo lo demás: cualquier línea de log
// que se emita a partir de aquí —incluida la del handler de errores— lleva el
// CorrelationId. Ponerlo más abajo dejaría sin identificar justamente los fallos
// tempranos, que son los peores de diagnosticar.
app.UseMiddleware<CorrelationIdMiddleware>();

// Una línea por request con método, ruta, código y duración, en vez de las tres
// del logger por defecto.
app.UseSerilogRequestLogging();

// Lo más arriba posible: solo captura lo que ocurre DESPUÉS de él.
app.UseExceptionHandler();

// Convierte los 401/403/404 "vacíos" que emite el framework (los que no pasan por
// una excepción) en ProblemDetails, para que el cliente reciba SIEMPRE el mismo
// formato de error venga de donde venga.
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

// HSTS: le dice al navegador "a este dominio, solo HTTPS" durante N meses, así que la
// PRIMERA petición en claro de la siguiente visita ni siquiera sale. UseHttpsRedirection
// por sí solo no lo evita: redirige, pero esa primera petición ya viajó con la cookie o
// el token dentro.
//
// ⚠️ Fuera de Development a propósito. En local se sirve HTTP y una cabecera HSTS queda
// cacheada en el navegador para `localhost`, rompiendo cualquier otro proyecto que use
// ese host en claro — y cuesta de diagnosticar porque el fallo aparece en otra app.
if (!app.Environment.IsDevelopment())
    app.UseHsts();

app.UseHttpsRedirection();

// UseCors va ANTES de la autenticación: un preflight OPTIONS no lleva token y
// tiene que poder responderse sin pasar por el filtro de autorización.
app.UseCors(CorsPolicies.Default);

// Antes de auth: rechazar una avalancha es más barato que validar su token.
app.UseRateLimiter();

// Sirve wwwroot/ (las imágenes de producto). Va DESPUÉS de CORS y del rate limiter,
// y antes de auth porque son públicas. El orden importa: UseStaticFiles es TERMINAL
// para los archivos que sirve, así que puesto más arriba las imágenes no pasaban por
// el limitador (descarga en bucle sin cuota) ni recibían cabeceras CORS (un <img>
// no las necesita, pero un fetch() del front sí).
app.UseStaticFiles();

// El orden importa: primero se averigua QUIÉN eres, después QUÉ puedes hacer.
app.UseAuthentication();
app.UseAuthorization();

// endpoints ----
app.MapControllers();

// Readiness: 200 solo si SQL Server y Redis responden. `/health` (liveness) lo sirve
// HealthController y no toca ninguna dependencia externa a propósito — si la sonda de
// vida depende de la base, una caída de la base provoca que el orquestador reinicie
// procesos que están perfectamente sanos.
app.MapHealthChecks("/health/ready", new HealthCheckOptions
{
    // Filtra por tag: hoy todos los checks lo llevan, pero sin el predicado los tags
    // eran decorativos y un check futuro sin tag entraría en readiness sin querer.
    Predicate = check => check.Tags.Contains("ready")
});

app.Run();


/// <summary>
/// Program es una clase <b>generada</b> por las instrucciones de nivel superior, y por
/// eso nace <c>internal</c>: <c>WebApplicationFactory&lt;Program&gt;</c> no la ve y el
/// proyecto de tests no compila. Declararla <c>public partial</c> aquí es la forma
/// oficial de abrirla sin tocar nada más.
/// </summary>
public partial class Program;
