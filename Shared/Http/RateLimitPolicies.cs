using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;
using Microsoft.Extensions.Options;

namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Limitación de peticiones, integrada en .NET 9 (<c>System.Threading.RateLimiting</c>).
/// No hace falta ningún paquete.
/// </summary>
/// <remarks>
/// Dos políticas y no una: el límite global protege la API de un cliente pesado,
/// pero el que de verdad importa es el de <c>auth</c> — sin él, el lockout de Identity
/// se puede sortear probando contraseñas contra <b>muchos</b> usuarios distintos,
/// que es como se hace el password spraying.
/// </remarks>
public static class RateLimitPolicies
{
  /// <summary>Política estricta para login y registro.</summary>
  public const string Auth = "auth";

  public static IServiceCollection AddRateLimiting(
      this IServiceCollection services, IConfiguration configuration)
  {
    services.AddOptions<RateLimitOptions>()
        .Bind(configuration.GetSection(RateLimitOptions.SectionName))
        .ValidateDataAnnotations()
        .ValidateOnStart();

    services.AddRateLimiter(options =>
    {
      // 429 con ProblemDetails, no un cuerpo vacío.
      options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

      // Global: ventana fija por IP.
      options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
      {
        var limits = Limits(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
              PermitLimit = limits.GlobalPermitLimit,
              Window = TimeSpan.FromSeconds(limits.GlobalWindowSeconds),
              QueueLimit = 0
            });
      });

      options.AddPolicy(Auth, context =>
      {
        var limits = Limits(context);

        return RateLimitPartition.GetFixedWindowLimiter(
            partitionKey: ClientKey(context),
            factory: _ => new FixedWindowRateLimiterOptions
            {
              PermitLimit = limits.AuthPermitLimit,
              Window = TimeSpan.FromSeconds(limits.AuthWindowSeconds),
              QueueLimit = 0
            });
      });
    });

    return services;
  }

  /// <summary>
  /// Los límites se resuelven del contenedor por petición y no se capturan en un
  /// <c>IConfiguration</c> leído al registrar: la regla del proyecto es que los servicios
  /// reciban <c>IOptions&lt;T&gt;</c>, y así un cambio de configuración en caliente se
  /// respeta en la siguiente partición que se cree.
  /// </summary>
  private static RateLimitOptions Limits(HttpContext context)
      => context.RequestServices.GetRequiredService<IOptions<RateLimitOptions>>().Value;

  /// <summary>
  /// Partición por IP remota. Detrás de un proxy hay que activar
  /// <c>UseForwardedHeaders</c> o todas las peticiones compartirán la IP del proxy y
  /// el límite se agotará para todo el mundo a la vez.
  /// </summary>
  private static string ClientKey(HttpContext context)
      => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
