using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ApiEcommerce.Shared.Auth;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Puerta de admisión para peticiones duplicadas en vuelo: si ya hay otra idéntica
/// ejecutándose, devuelve 409 en vez de dejar que se apile sobre la misma fila.
/// </summary>
/// <remarks>
/// No es la garantía de idempotencia —esa es <see cref="ICommandLog"/>, que escribe en la
/// misma transacción que el efecto—, sino una optimización delante: quitarlo sigue siendo
/// correcto, solo más lento bajo tormentas de reintentos. Aquí solo vive protocolo.
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
  /// <summary>Cabecera con la que el cliente declara su intento.</summary>
  public const string HeaderName = "Idempotency-Key";

  /// <summary>Marca una respuesta que reproduce el resultado de un intento anterior.</summary>
  /// <remarks>
  /// La pone el controller a partir de <c>CommandOutcome.WasReplayed</c>, no este filtro:
  /// emitirla desde los dos sitios marcaría solo los replays que resolviera el atajo.
  /// </remarks>
  public const string ReplayedHeader = "Idempotency-Replayed";

  /// <summary>Orden explícito para quedar por fuera de otros filtros del proyecto.</summary>
  public int Order => -100;

  /// <summary>Cruza la puerta antes de la acción y suelta el marcador al terminar.</summary>
  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);

    var request = context.HttpContext.Request;

    // La idempotencia la pide el cliente, y omitir la cabecera es su vía de escape.
    if (!request.Headers.TryGetValue(HeaderName, out var header) || string.IsNullOrWhiteSpace(header))
    {
      await next();
      return;
    }

    // Sin usuario no se puede acotar la clave, y dos clientes con la misma se pisarían.
    var user = context.HttpContext.User.GetUserId();

    if (string.IsNullOrEmpty(user))
    {
      await next();
      return;
    }

    var services = context.HttpContext.RequestServices;
    var options = services.GetRequiredService<IOptions<IdempotencyOptions>>().Value;
    var metrics = services.GetRequiredService<IdempotencyMetrics>();

    var clientKey = header.ToString();

    // La clave la elige el cliente y acaba en la primaria de ExecutedCommands: se acota.
    if (clientKey.Length > options.MaxKeyLength)
    {
      metrics.InvalidKey();

      context.Result = new BadRequestObjectResult(new ProblemDetails
      {
        Status = StatusCodes.Status400BadRequest,
        Title = "Invalid idempotency key",
        Detail = $"The {HeaderName} must not exceed {options.MaxKeyLength} characters.",
        Extensions = { ["code"] = "idempotency_key_invalid" }
      });

      return;
    }

    // Con QueryString (`Path` no lo lleva) y en minúsculas, para no partir ni fundir puertas.
    var route = $"{request.Method}:{request.Path.Value?.ToLowerInvariant()}{request.QueryString.Value}";
    var key = $"{user}:{route}:{clientKey}";

    var store = services.GetRequiredService<IIdempotencyStore>();
    var gate = await store.TryEnterAsync(key, options.ReservationTtl, context.HttpContext.RequestAborted);

    if (gate.Outcome is IdempotencyGateOutcome.Busy)
    {
      metrics.InProgress();

      // 409 y no 429: no es exceso de tráfico, es la misma operación duplicada.
      context.Result = new ConflictObjectResult(new ProblemDetails
      {
        Status = StatusCodes.Status409Conflict,
        Title = "Duplicate request in progress",
        Detail = $"Another request with the same {HeaderName} is still being processed.",
        Extensions = { ["code"] = "idempotency_in_progress" }
      });

      return;
    }

    if (gate.Outcome is IdempotencyGateOutcome.Unavailable) metrics.GateUnavailable();

    try
    {
      await next();
    }
    finally
    {
      // En `finally` y con CancellationToken.None: también hay que soltar si la acción
      // lanza o el cliente cuelga, o la puerta queda cerrada hasta que caduque el TTL.
      await store.ReleaseAsync(key, gate.Fence, CancellationToken.None);
    }
  }
}
