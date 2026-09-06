using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Options;
using ApiEcommerce.Shared.Auth;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Puerta de admisión para peticiones duplicadas <b>en vuelo</b>: si ya hay otra idéntica
/// ejecutándose, devuelve 409 en vez de dejar que se apile sobre la misma fila.
/// </summary>
/// <remarks>
/// <para>
/// ⚠️ <b>Esto NO es la garantía de idempotencia</b>, aunque durante mucho tiempo lo fue.
/// Quien garantiza que una compra no se ejecuta dos veces es <see cref="ICommandLog"/>,
/// que escribe la marca del comando en la <b>misma transacción</b> que el efecto. Este
/// filtro es una optimización delante: se puede quitar y todo sigue siendo correcto,
/// solo que más lento bajo tormentas de reintentos.
/// </para>
/// <para>
/// Por qué se movió: un filtro HTTP no puede participar en la transacción de negocio, así
/// que la marca se confirmaba <i>después</i> del efecto. Eso dejaba una ventana en la que
/// la compra ocurría y nadie la recordaba —el proceso muere en medio de un despliegue y
/// el reintento del cliente vuelve a comprar— y otra en la que el almacén no respondía por
/// carga y la petición se ejecutaba sin protección: 174 de 14 400, medido, con Redis sano.
/// Ninguna de las dos se cierra con más código aquí arriba; se cierran cambiando dónde
/// vive la marca. Es la misma corrección que ya se hizo con <c>[Transactional]</c>, y por
/// la misma razón: la política de negocio no vive en un atributo del controller.
/// </para>
/// <para>
/// Lo que sí queda aquí es <b>protocolo</b>, que es lo único que un filtro debe saber:
/// leer la cabecera, validar su forma y traducir «hay otra igual en vuelo» a un 409.
/// Las respuestas memorizadas se fueron con la garantía — tenerlas aquí creaba una
/// segunda copia del cuerpo que no coincidía byte a byte con la real.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
  public const string HeaderName = "Idempotency-Key";

  /// <summary>Marca una respuesta que reproduce el resultado de un intento anterior.</summary>
  /// <remarks>
  /// La pone el <b>controller</b>, no este filtro, a partir del hecho que le reporta el
  /// servicio (<c>CommandOutcome.WasReplayed</c>). Aquí ya no se reproduce nada: si la
  /// cabecera se emitiera desde los dos sitios, marcaría solo los replays que resolviera
  /// el atajo, y una cabecera que <i>a veces</i> marca los replays es peor que ninguna.
  /// </remarks>
  public const string ReplayedHeader = "Idempotency-Replayed";

  /// <summary>Orden explícito para quedar por fuera de otros filtros del proyecto.</summary>
  public int Order => -100;

  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    ArgumentNullException.ThrowIfNull(context);
    ArgumentNullException.ThrowIfNull(next);

    var request = context.HttpContext.Request;

    // Sin cabecera no hay nada que hacer: la idempotencia la pide el cliente, que es quien
    // sabe si está reintentando. Y omitirla es la vía de escape estándar para un cliente
    // que prefiera seguir sin garantía — es lo que documenta Adyen.
    if (!request.Headers.TryGetValue(HeaderName, out var header) || string.IsNullOrWhiteSpace(header))
    {
      await next();
      return;
    }

    // Sin usuario identificado no se puede acotar la clave, y un espacio de nombres
    // compartido haría que dos clientes con la misma clave se pisaran.
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

    // La clave la elige el cliente y acaba en una clave de Redis y en la primaria de
    // ExecutedCommands. Sin límite se aceptaban claves de 7000 caracteres (medido).
    // Validar la FORMA de la petición es trabajo del adaptador, así que se queda aquí.
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

    // Se incluye el QueryString: `request.Path` no lo lleva, así que `POST /x?page=1` y
    // `POST /x?page=2` con la misma clave compartirían puerta. Y se normaliza a minúsculas
    // para que `/Product/buy` y `/product/buy` no generen dos.
    var route = $"{request.Method}:{request.Path.Value?.ToLowerInvariant()}{request.QueryString.Value}";
    var key = $"{user}:{route}:{clientKey}";

    var store = services.GetRequiredService<IIdempotencyStore>();
    var gate = await store.TryEnterAsync(key, options.ReservationTtl, context.HttpContext.RequestAborted);

    if (gate.Outcome is IdempotencyGateOutcome.Busy)
    {
      metrics.InProgress();

      // 409 y no 429: no es exceso de tráfico, es la misma operación duplicada.
      //
      // Se responde aquí en vez de dejar pasar a propósito. Sin esta puerta, N reintentos
      // simultáneos con la misma clave se quedarían todos bloqueados en la clave primaria
      // de ExecutedCommands —cada uno reteniendo su conexión— hasta que el primero
      // confirmara. Serían correctos, pero a costa del pool de conexiones.
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
      // En `finally` y con CancellationToken.None: el marcador debe soltarse también
      // cuando la acción lanza o cuando el cliente cuelga. Si no, una compra fallida
      // dejaría la puerta cerrada hasta que caducara el TTL y el cliente no podría
      // reintentar de verdad.
      await store.ReleaseAsync(key, gate.Fence, CancellationToken.None);
    }
  }
}
