using System.Threading.RateLimiting;
using Microsoft.AspNetCore.RateLimiting;

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

  public static IServiceCollection AddRateLimiting(this IServiceCollection services)
  {
    services.AddRateLimiter(options =>
    {
      // 429 con ProblemDetails, no un cuerpo vacío.
      options.RejectionStatusCode = StatusCodes.Status429TooManyRequests;

      // Global: ventana fija por IP.
      options.GlobalLimiter = PartitionedRateLimiter.Create<HttpContext, string>(context =>
          RateLimitPartition.GetFixedWindowLimiter(
              partitionKey: ClientKey(context),
              factory: _ => new FixedWindowRateLimiterOptions
              {
                PermitLimit = 100,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
              }));

      options.AddPolicy(Auth, context =>
          RateLimitPartition.GetFixedWindowLimiter(
              partitionKey: ClientKey(context),
              factory: _ => new FixedWindowRateLimiterOptions
              {
                PermitLimit = 10,
                Window = TimeSpan.FromMinutes(1),
                QueueLimit = 0
              }));
    });

    return services;
  }

  /// <summary>
  /// Partición por IP remota. Detrás de un proxy hay que activar
  /// <c>UseForwardedHeaders</c> o todas las peticiones compartirán la IP del proxy y
  /// el límite se agotará para todo el mundo a la vez.
  /// </summary>
  private static string ClientKey(HttpContext context)
      => context.Connection.RemoteIpAddress?.ToString() ?? "unknown";
}
