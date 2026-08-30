using ApiEcommerce.Data;
using ApiEcommerce.Models;
using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Authentication.JwtBearer;
using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Service.Auth;


/// <summary>
/// Identity + JWT: quién eres (autenticación) y qué puedes hacer (autorización).
/// Equivale al <c>SecurityFilterChain</c> + <c>UserDetailsService</c> de Spring Security.
/// </summary>
public static class AuthExtensions
{
  public static IServiceCollection AddIdentityAndJwt(
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

    return services;
  }
}
