using ApiEcommerce.Shared.DependencyInjection;
using ApiEcommerce.Shared.Http;
using Asp.Versioning.ApiExplorer;
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
// Cada bloque vive en Shared/DependencyInjection/ServiceCollectionExtensions.cs
builder.Services.AddPersistence(builder.Configuration);   // EF Core + SQL Server
builder.Services.AddObjectMapping();                      // AutoMapper (un Profile por entidad)
builder.Services.AddRepositories();                       // IBaseRepository<> + repos por entidad
builder.Services.AddApplicationServices(builder.Configuration);  // CrudService + reglas + servicios
builder.Services.AddAuthenticationAndAuthorization(builder.Configuration);  // Identity + JWT
builder.Services.AddCaching(builder.Configuration);       // Redis + decorador cache-aside
builder.Services.AddCorsPolicy(builder.Configuration);    // orígenes permitidos (desde config)
builder.Services.AddApiVersioningAndDocs();               // /api/v1/... + un Swagger por versión
builder.Services.AddRateLimiting();                       // límite global + política 'auth'
builder.Services.AddHealthProbes(builder.Configuration);  // /health/ready (SQL Server + Redis)
builder.Services.AddErrorHandling();                      // GlobalExceptionHandler + ProblemDetails

// Controllers ----
builder.Services.AddControllers();

var app = builder.Build();


// // // Datos de arranque -----------------------------------------------
// Scope propio: el contenedor RAÍZ no puede resolver servicios Scoped
// (AppDbContext, UserManager) y lanzaría en el arranque.
using (var scope = app.Services.CreateScope())
{
    await ApiEcommerce.Data.DataSeeder.SeedAsync(scope.ServiceProvider);
}


// // // Configure the HTTP request pipeline ------------------------------
// El orden del pipeline NO es decorativo: cada middleware solo ve lo que ocurre
// después de él. De arriba abajo: errores → swagger → https → cors → auth → endpoints.

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

app.UseHttpsRedirection();

// Sirve wwwroot/ (las imágenes de producto). Va antes de auth: son públicas.
app.UseStaticFiles();

// UseCors va ANTES de la autenticación: un preflight OPTIONS no lleva token y
// tiene que poder responderse sin pasar por el filtro de autorización.
app.UseCors(CorsPolicies.Default);

// Antes de auth: rechazar una avalancha es más barato que validar su token.
app.UseRateLimiter();

// El orden importa: primero se averigua QUIÉN eres, después QUÉ puedes hacer.
app.UseAuthentication();
app.UseAuthorization();

// endpoints ----
app.MapControllers();

// Readiness: 200 solo si SQL Server y Redis responden. `/health` (liveness) lo sirve
// HealthController y no toca ninguna dependencia externa a propósito — si la sonda de
// vida depende de la base, una caída de la base provoca que el orquestador reinicie
// procesos que están perfectamente sanos.
app.MapHealthChecks("/health/ready");

app.Run();
