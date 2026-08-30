using ApiEcommerce.Data;
using ApiEcommerce.Mapping;
using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Repository;
using ApiEcommerce.Service;
using ApiEcommerce.Service.Auth;
using ApiEcommerce.Service.Crud;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Caching;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Storage;
using Asp.Versioning;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using Microsoft.IdentityModel.Tokens;
using Swashbuckle.AspNetCore.SwaggerGen;
using System.Text;

namespace ApiEcommerce.Shared.DependencyInjection;


/// <summary>
/// Registro de dependencias agrupado por capa, para que <c>Program.cs</c> se lea
/// como un índice y no crezca a 200 líneas.
/// Todo lo que dependa de <see cref="AppDbContext"/> va con lifetime <b>Scoped</b>:
/// el DbContext vive lo que dura el request y capturarlo en un singleton corrompería
/// el change tracker.
/// </summary>
public static class ServiceCollectionExtensions
{

  /// <summary>EF Core + SQL Server.</summary>
  public static IServiceCollection AddPersistence(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddDbContext<AppDbContext>(options =>
        options.UseSqlServer(configuration.GetConnectionString("ConexionSql")));

    return services;
  }

  /// <summary>AutoMapper: registra todos los Profile del assembly donde vive <see cref="CategoryProfile"/>.</summary>
  public static IServiceCollection AddObjectMapping(this IServiceCollection services)
  {
    services.AddAutoMapper(cfg => { }, typeof(CategoryProfile).Assembly);
    return services;
  }

  /// <summary>Repositorios: el genérico abierto + uno por entidad.</summary>
  public static IServiceCollection AddRepositories(this IServiceCollection services)
  {
    // permite inyectar IBaseRepository<X> sin escribir un repositorio específico
    services.AddScoped(typeof(IBaseRepository<>), typeof(BaseRepository<>));

    services.AddScoped<ICategoryRepository, CategoryRepository>();
    services.AddScoped<IProductRepository, ProductRepository>();

    return services;
  }

  /// <summary>Servicios de aplicación: CRUD compuesto, reglas por entidad y servicios por entidad.</summary>
  public static IServiceCollection AddApplicationServices(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<FileStorageOptions>()
        .Bind(configuration.GetSection(FileStorageOptions.SectionName))
        .ValidateDataAnnotations();

    // ---- reglas de negocio ------------------------------------------------
    // Por defecto, "sin reglas". Se registra como genérico abierto para que una
    // entidad nueva funcione sin escribir nada.
    services.AddScoped(typeof(IEntityRules<,,>), typeof(NoEntityRules<,,>));

    // Registro CERRADO por entidad: el contenedor prefiere siempre la coincidencia
    // exacta sobre el genérico abierto, así que estas ganan sobre NoEntityRules.
    services.AddScoped<IEntityRules<Category, CreateCategoryDto, UpdateCategoryDto>, CategoryRules>();
    services.AddScoped<IEntityRules<Product, CreateProductDto, UpdateProductDto>, ProductRules>();

    // ---- CRUD compuesto ---------------------------------------------------
    // ICrudService no lleva TEntity (para que el controller no pueda ver la entidad),
    // así que el registro es cerrado: una línea por entidad.
    services.AddScoped<
        ICrudService<CategoryDto, CreateCategoryDto, UpdateCategoryDto>,
        CrudService<Category, CategoryDto, CreateCategoryDto, UpdateCategoryDto>>();

    services.AddScoped<
        ICrudService<ProductDto, CreateProductDto, UpdateProductDto>,
        CrudService<Product, ProductDto, CreateProductDto, UpdateProductDto>>();

    // ---- infraestructura de aplicación ------------------------------------
    services.AddScoped<IFileStorage, LocalFileStorage>();

    // ---- servicios por entidad -------------------------------------------
    // Category se registra DECORADO: el contenedor construye el servicio real y lo
    // envuelve en el que cachea. Quien pide ICategoryService recibe el decorador y
    // no se entera. (Con Scrutor sería services.Decorate<...>(); a mano son 3 líneas
    // y no se añade una dependencia por eso.)
    services.AddScoped<CategoryService>();
    services.AddScoped<ICategoryService>(sp => new CachedCategoryService(
        sp.GetRequiredService<CategoryService>(),
        sp.GetRequiredService<ICacheService>()));

    services.AddScoped<IProductService, ProductService>();

    return services;
  }


  /// <summary>
  /// Identity + JWT: quién eres (autenticación) y qué puedes hacer (autorización).
  /// Equivale al <c>SecurityFilterChain</c> + <c>UserDetailsService</c> de Spring Security.
  /// </summary>
  public static IServiceCollection AddAuthenticationAndAuthorization(
      this IServiceCollection services, IConfiguration configuration)
  {
    // ---- configuración tipada y validada AL ARRANQUE ----------------------
    // ValidateOnStart hace que un secreto ausente o corto reviente el arranque,
    // no el primer login. Es el equivalente a @ConfigurationProperties + @Validated.
    services.AddOptions<JwtOptions>()
        .Bind(configuration.GetSection(JwtOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // ---- Identity ---------------------------------------------------------
    // AddIdentityCore y no AddIdentity: esta es una API stateless con Bearer token.
    // AddIdentity registraría además los esquemas de cookie de Identity, que nadie
    // usa y que se pelean con el esquema JWT por ser el "default".
    services.AddIdentityCore<ApplicationUser>(options =>
    {
      // Política de contraseñas explícita: los defaults de Identity son razonables,
      // pero dejarlos implícitos hace que nadie sepa cuál es la política real.
      options.Password.RequiredLength = 8;
      options.Password.RequireDigit = true;
      options.Password.RequireLowercase = true;
      options.Password.RequireUppercase = true;
      options.Password.RequireNonAlphanumeric = false;

      options.User.RequireUniqueEmail = true;

      // Bloqueo por fuerza bruta. Sin esto, CheckPasswordSignInAsync(lockoutOnFailure: true)
      // cuenta los fallos pero nunca bloquea.
      options.Lockout.MaxFailedAccessAttempts = 5;
      options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
      options.Lockout.AllowedForNewUsers = true;
    })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddSignInManager()          // necesario para CheckPasswordSignInAsync + lockout
        .AddDefaultTokenProviders();

    // ---- autenticación por Bearer token -----------------------------------
    services.AddAuthentication(options =>
    {
      options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
      options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer(options =>
    {
      var jwt = configuration.GetSection(JwtOptions.SectionName).Get<JwtOptions>()!;

      options.SaveToken = true;

      options.TokenValidationParameters = new TokenValidationParameters
      {
        // Las CUATRO validaciones activas. El código de referencia apagaba
        // ValidateIssuer y ValidateAudience porque su token no emitía `iss`/`aud`:
        // eso es parchear el síntoma. Un token firmado con la misma clave por
        // cualquier otro servicio sería aceptado aquí.
        ValidateIssuerSigningKey = true,
        IssuerSigningKey = new SymmetricSecurityKey(Encoding.UTF8.GetBytes(jwt.SecretKey)),

        ValidateIssuer = true,
        ValidIssuer = jwt.Issuer,

        ValidateAudience = true,
        ValidAudience = jwt.Audience,

        ValidateLifetime = true,
        // El default son 5 minutos de gracia: un token expirado seguiría valiendo
        // 5 minutos más. Con reloj sincronizado, 30 s sobra.
        ClockSkew = TimeSpan.FromSeconds(30)
      };
    });

    services.AddAuthorization();

    // ---- datos de arranque -------------------------------------------------
    services.AddOptions<SeedOptions>()
        .Bind(configuration.GetSection(SeedOptions.SectionName))
        .ValidateDataAnnotations();

    // ---- servicios de aplicación de auth ----------------------------------
    // Singleton: no toca la base y materializa la clave de firma una sola vez.
    services.AddSingleton<IJwtTokenService, JwtTokenService>();
    services.AddScoped<IAuthService, AuthService>();

    return services;
  }


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
      options.AddPolicy(CorsPolicies.Default, policy =>
      {
        policy.WithOrigins(origins)
              .AllowAnyMethod()
              .AllowAnyHeader()
              // Deja que el front lea la cabecera de paginación y las de versión.
              .WithExposedHeaders("api-supported-versions", "api-deprecated-versions");
      });
    });

    return services;
  }


  /// <summary>
  /// Cache distribuida en Redis + el decorador que la aplica al catálogo.
  /// </summary>
  /// <remarks>
  /// Si <c>Redis:Configuration</c> viene vacío se registra <see cref="NoCacheService"/>
  /// y todo sigue funcionando sin cache: el proyecto arranca en una máquina sin Redis.
  /// </remarks>
  public static IServiceCollection AddCaching(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<CacheOptions>()
        .Bind(configuration.GetSection(CacheOptions.SectionName))
        .ValidateDataAnnotations();

    var options = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>() ?? new CacheOptions();

    if (options.IsEnabled)
    {
      services.AddStackExchangeRedisCache(redis =>
      {
        redis.Configuration = options.Configuration;
        redis.InstanceName = options.InstanceName;
      });

      services.AddScoped<ICacheService, RedisCacheService>();
    }
    else
    {
      // Null Object: los decoradores siguen compilando y ejecutando sin un solo `if`.
      services.AddSingleton<ICacheService, NoCacheService>();
    }

    return services;
  }


  /// <summary>
  /// Sondas de salud. <c>/health</c> es <b>liveness</b> (¿el proceso responde?) y lo
  /// sirve <c>HealthController</c>; <c>/health/ready</c> es <b>readiness</b> (¿las
  /// dependencias están arriba?) y es el que debe mirar un orquestador antes de
  /// mandarle tráfico.
  /// </summary>
  public static IServiceCollection AddHealthProbes(
      this IServiceCollection services, IConfiguration configuration)
  {
    var checks = services.AddHealthChecks()
        .AddDbContextCheck<AppDbContext>(name: "sqlserver", tags: ["ready"]);

    var redis = configuration.GetSection(CacheOptions.SectionName).Get<CacheOptions>();

    if (redis is not null && redis.IsEnabled)
      checks.AddRedis(redis.Configuration, name: "redis", tags: ["ready"]);

    return services;
  }

  /// <summary>
  /// Handler global de errores + ProblemDetails (RFC 7807).
  /// Equivalente a <c>@ControllerAdvice</c> de Spring.
  /// </summary>
  public static IServiceCollection AddErrorHandling(this IServiceCollection services)
  {
    services.AddProblemDetails();
    services.AddExceptionHandler<GlobalExceptionHandler>();

    return services;
  }

}
