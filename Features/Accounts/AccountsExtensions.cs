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
/// Registro del slice <b>Cuentas</b>: identidad, autenticación, autorización y sesiones.
/// </summary>
public static class AccountsExtensions
{
  /// <summary>Registra opciones, Identity, el esquema Bearer y los servicios del slice.</summary>
  public static IServiceCollection AddAccountsFeature(
      this IServiceCollection services, IConfiguration configuration)
  {
    // ValidateOnStart: un secreto ausente o corto revienta el arranque, no el primer login.
    services.AddOptions<JwtOptions>()
        .Bind(configuration.GetSection(JwtOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    // AddIdentityCore y no AddIdentity: esta última registra esquemas de cookie que nadie usa
    // y que se pelean con el esquema JWT por ser el "default".
    services.AddIdentityCore<ApplicationUser>(options =>
    {
      // Política explícita aunque coincida con los defaults, para que sea legible.
      options.Password.RequiredLength = 8;
      options.Password.RequireDigit = true;
      options.Password.RequireLowercase = true;
      options.Password.RequireUppercase = true;
      options.Password.RequireNonAlphanumeric = false;

      options.User.RequireUniqueEmail = true;

      // Sin esto, CheckPasswordSignInAsync(lockoutOnFailure: true) cuenta fallos pero no bloquea.
      options.Lockout.MaxFailedAccessAttempts = 5;
      options.Lockout.DefaultLockoutTimeSpan = TimeSpan.FromMinutes(5);
      options.Lockout.AllowedForNewUsers = true;
    })
        .AddRoles<IdentityRole>()
        .AddEntityFrameworkStores<AppDbContext>()
        .AddSignInManager()          // necesario para CheckPasswordSignInAsync + lockout
        .AddDefaultTokenProviders();

    // La validación del token la configura ConfigureJwtBearerOptions, que recibe las JwtOptions
    // ya validadas por DI en vez de releer la configuración aquí.
    services.ConfigureOptions<ConfigureJwtBearerOptions>();

    services.AddAuthentication(options =>
    {
      options.DefaultAuthenticateScheme = JwtBearerDefaults.AuthenticationScheme;
      options.DefaultChallengeScheme = JwtBearerDefaults.AuthenticationScheme;
    })
    .AddJwtBearer();

    services.AddAuthorization();

    // Singleton, con la contrapartida de que rotar Jwt:SecretKey exige reiniciar el proceso.
    services.AddSingleton<IJwtTokenService, JwtTokenService>();
    services.AddScoped<IAuthService, AuthService>();

    // Sesiones revocables: Scoped porque escriben en la base dentro de la transacción del
    // request. La cookie sería válida como Singleton; se deja Scoped por uniformidad.
    services.AddOptions<RefreshTokenOptions>()
        .Bind(configuration.GetSection(RefreshTokenOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddScoped<IRefreshTokenRepository, RefreshTokenRepository>();
    services.AddScoped<IRefreshTokenService, RefreshTokenService>();
    services.AddScoped<IUserAdminService, UserAdminService>();
    services.AddScoped<RefreshTokenCookie>();

    // En todos los entornos: la tabla crece haya o no actividad.
    services.AddHostedService<RefreshTokenCleaner>();

    return services;
  }
}
