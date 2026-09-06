using ApiEcommerce.Data;
using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;
using ApiEcommerce.Features.Accounts.Models;
using ApiEcommerce.Features.Accounts.Service;
using ApiEcommerce.Features.Accounts.Repository;

namespace ApiEcommerce.Features.Accounts;


/// <summary>
/// Registro del contexto acotado <b>Cuentas</b>: identidad, autenticación y autorización.
/// Equivale al <c>SecurityFilterChain</c> + <c>UserDetailsService</c> de Spring Security.
/// </summary>
/// <remarks>
/// <c>ApplicationUser</c> vive aquí y <b>no</b> implementa <c>IEntity</c>: su clave es un
/// <c>string</c> y su ciclo de vida lo gobierna <c>UserManager</c>, no el CRUD genérico.
/// Por eso este slice no tiene carpeta <c>Repository/</c>.
/// </remarks>
public static class AccountsExtensions
{
  public static IServiceCollection AddAccountsFeature(
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
    // La validación del token la configura ConfigureJwtBearerOptions, que recibe las
    // JwtOptions ya validadas por DI en vez de volver a leer la configuración aquí.
    services.ConfigureOptions<ConfigureJwtBearerOptions>();

    services.AddAuthentication(options =>
    {
      options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
      options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

    services.AddAuthorization();

    // ---- servicios de aplicación de auth ----------------------------------
    // Singleton: no toca la base y materializa la clave de firma una sola vez.
    // Contrapartida asumida: rotar Jwt:SecretKey exige reiniciar el proceso. El día
    // que haga falta rotación en caliente, se cambia IOptions por IOptionsMonitor.
    services.AddSingleton<IJwtTokenService, JwtTokenService>();
    services.AddScoped<IAuthService, AuthService>();

    // Sesiones revocables. Scoped porque escriben en la base dentro de la transacción
    // del request; la cookie es un adaptador sin estado, pero depende de IOptions y del
    // entorno, así que Singleton bastaría — se deja Scoped por uniformidad con el resto
    // del slice y porque no se instancia en caliente.
    services.AddOptions<RefreshTokenOptions>()
        .Bind(configuration.GetSection(RefreshTokenOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
    services.AddScoped<IRefreshTokenService, RefreshTokenService>();
    services.AddScoped<RefreshTokenCookie>();

    // Siempre, como el resto de purgas: la tabla crece haya o no actividad.
    services.AddHostedService<RefreshTokenCleaner>();

    return services;
  }
}
