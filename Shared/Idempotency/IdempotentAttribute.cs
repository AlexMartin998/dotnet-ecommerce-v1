using System.Security.Cryptography;
using System.Text.Json;
using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Formatters;
using Microsoft.Extensions.Options;
using Microsoft.AspNetCore.Mvc.Filters;

namespace ApiEcommerce.Shared.Idempotency;


/// <summary>
/// Hace idempotente una acción mutante: si el cliente manda la cabecera
/// <c>Idempotency-Key</c>, un reintento con la misma clave <b>devuelve la respuesta
/// original</b> en vez de volver a ejecutar la operación.
/// </summary>
/// <remarks>
/// <para>
/// Es la misma forma que <c>[Transactional]</c>: un <see cref="IAsyncActionFilter"/>
/// que envuelve la acción, para que el controller siga sin saber nada de esto.
/// </para>
/// <para>
/// La clave se compone con <b>usuario + método + ruta + clave del cliente</b>. Sin el
/// usuario, dos clientes que casualmente generen el mismo GUID se pisarían — y peor,
/// uno recibiría la respuesta del otro, que es una fuga de datos entre cuentas.
/// </para>
/// <para>
/// Sin cabecera no hace nada: la idempotencia es opcional y la pide el cliente, que es
/// quien sabe si está reintentando.
/// </para>
/// </remarks>
[AttributeUsage(AttributeTargets.Class | AttributeTargets.Method)]
public sealed class IdempotentAttribute : Attribute, IAsyncActionFilter, IOrderedFilter
{
  public const string HeaderName = "Idempotency-Key";

  /// <summary>Marca una respuesta reproducida desde el almacén.</summary>
  public const string ReplayedHeader = "Idempotency-Replayed";

  /// <summary>
  /// Avisa al cliente de que la operación se ejecutó <b>sin</b> garantía de idempotencia.
  /// </summary>
  /// <remarks>
  /// El almacén degrada en abierto: si no contesta, la petición se ejecuta igual en vez
  /// de devolver un 500 por una compra que sí se cobró. Pero el cliente no tenía forma
  /// de saberlo, y "reintentar es seguro" dejaba de ser cierto sin que nadie se enterara.
  /// Sólo se emite cuando la garantía NO se aplicó, así que su mera presencia es la señal.
  /// </remarks>
  public const string UnguaranteedHeader = "Idempotency-Guaranteed";

  /// <summary>
  /// Orden explícito para quedar POR FUERA de <c>[Transactional]</c> (Order 0).
  /// </summary>
  /// <remarks>
  /// El anidamiento correcto es idempotencia fuera, transacción dentro: así la
  /// respuesta se memoriza <b>después</b> del commit. Invertido, un rollback dejaría
  /// memorizada 24 h una respuesta de éxito para una operación que nunca ocurrió.
  /// Sin <c>IOrderedFilter</c> ambos filtros tenían <c>Order = 0</c> y MVC los
  /// desempataba por el orden de reflexión de los atributos, que no está garantizado.
  /// </remarks>
  public int Order => -100;

  private static readonly JsonSerializerOptions SerializerOptions = new(JsonSerializerDefaults.Web);

  public async Task OnActionExecutionAsync(ActionExecutingContext context, ActionExecutionDelegate next)
  {
    var request = context.HttpContext.Request;

    if (!request.Headers.TryGetValue(HeaderName, out var header)
        || string.IsNullOrWhiteSpace(header))
    {
      await next();
      return;
    }

    // Sin usuario identificado NO se aplica idempotencia. Un espacio de nombres
    // "anonymous" compartido haría que dos clientes distintos con la misma clave se
    // reprodujeran la respuesta el uno al otro: exactamente la fuga entre cuentas que
    // el usuario en la clave viene a evitar.
    var user = context.HttpContext.User.GetUserId();

    if (string.IsNullOrEmpty(user))
    {
      await next();
      return;
    }

    var services = context.HttpContext.RequestServices;
    var store = services.GetRequiredService<IIdempotencyStore>();
    var metrics = services.GetRequiredService<IdempotencyMetrics>();
    var options = services.GetRequiredService<IOptions<IdempotencyOptions>>().Value;

    var clientKey = header.ToString();

    // La clave la elige el cliente y acaba entera dentro de una clave de Redis que vive
    // 24 h, en una instancia compartida con otros proyectos. Sin este límite se aceptaban
    // claves de 7000 caracteres (medido).
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

    // Se incluye el QueryString: `request.Path` no lo lleva, así que `POST /x?page=1`
    // y `POST /x?page=2` con la misma clave colisionaban. Y se normaliza a minúsculas
    // para que `/Product/buy` y `/product/buy` no generen dos claves.
    var route = $"{request.Method}:{request.Path.Value?.ToLowerInvariant()}{request.QueryString.Value}";
    var key = $"{user}:{route}:{clientKey}";

    var requestHash = HashOf(context);

    // Reservar PRIMERO, y leer sólo lo que la reserva devuelva.
    //
    // Antes esto era un GetAsync seguido de un TryAcquireAsync, y el orden importaba de
    // dos maneras. Costaba un viaje de más a Redis por petición —la presión que hace que
    // una ráfaga agote el timeout y la garantía se apague sola— y, sobre todo, dejaba un
    // camino que reproducía la respuesta SIN comparar la huella del cuerpo: el de quien
    // perdía la reserva por poco. Ahora el estado existente sólo puede llegar por aquí,
    // así que la comprobación de más abajo lo cubre TODO.
    var acquisition = await store.TryAcquireAsync(
        key, requestHash, options.ReservationTtl, context.HttpContext.RequestAborted);

    if (acquisition.Outcome is IdempotencyOutcome.Existing)
    {
      var existing = acquisition.Entry!;

      // Misma clave, otro cuerpo: 422, tanto si la primera terminó como si sigue en curso.
      if (MismatchedBody(context, existing, requestHash))
      {
        metrics.BodyMismatch();
        return;
      }

      // Terminada: se reproduce su respuesta.
      if (existing.Response is { } cached)
      {
        metrics.Replayed();
        Replay(context, cached);
        return;
      }

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

    // El almacén no contestó. Se ejecuta igual —degradar en abierto es la decisión del
    // proyecto y no cambia aquí— pero deja de ser invisible: se cuenta y se avisa.
    var guaranteed = acquisition.Outcome is IdempotencyOutcome.Acquired;

    if (!guaranteed)
    {
      metrics.Unguaranteed();
      context.HttpContext.Response.Headers[UnguaranteedHeader] = "false";
    }

    var executed = await next();

    // 3) Solo se memoriza el ÉXITO, y solo los tipos de resultado que se saben
    //    reproducir. Un 4xx/5xx se libera para que el cliente pueda reintentar de
    //    verdad: memorizar un error convertiría un fallo transitorio en permanente
    //    durante 24 h. Un resultado desconocido (FileResult, RedirectResult...) se
    //    libera también, en vez de memorizarse como un 200 vacío que sería mentira.
    int? status = executed.Result switch
    {
      ObjectResult o => o.StatusCode ?? StatusCodes.Status200OK,
      StatusCodeResult s => s.StatusCode,
      _ => null
    };

    if (executed.Exception is not null || status is null or (< 200) or (>= 300))
    {
      await store.ReleaseAsync(key, acquisition.Fence, CancellationToken.None);
      return;
    }

    metrics.Executed();

    string? body = null;
    string? contentType = null;

    if (executed.Result is ObjectResult { Value: not null } ok)
    {
      // ⚠️ Con las opciones de MVC, no con las nuestras. El contrato de la idempotencia es
      // "reproducir la respuesta ORIGINAL", y eso significa los MISMOS BYTES. Con un
      // JsonSerializerOptions propio, el replay salía equivalente pero no idéntico: un
      // `+` dentro de un base64 (el ETag de RowVersion, sin ir más lejos) se escapaba
      // como `\u002B` en el replay y no en la respuesta viva. Lo destapó un test que
      // comparaba los dos cuerpos, no una revisión.
      var jsonOptions = context.HttpContext.RequestServices
          .GetRequiredService<IOptions<JsonOptions>>().Value.JsonSerializerOptions;

      body = JsonSerializer.Serialize(ok.Value, jsonOptions);
      contentType = "application/json";
    }

    // ⚠️ Aquí NO se puede capturar el `Location` de un 201, aunque durante un tiempo
    // hubo código que lo intentaba y documentación que lo daba por resuelto.
    //
    // Cuando un IAsyncActionFilter recupera el control tras `await next()`, MVC todavía
    // no ha EJECUTADO el IActionResult: `CreatedAtRouteResult` escribe la cabecera en
    // `ExecuteResultAsync`, que corre después de todos los filtros de acción. Leer
    // `Response.Headers["Location"]` en este punto devuelve siempre vacío, así que se
    // memorizaba `Headers = null` y el replay de un create nunca llevaba Location.
    //
    // Hoy no afecta a nadie: el único uso de [Idempotent] es POST /product/buy, que
    // devuelve `Ok(...)`. Se deja escrito para que la próxima acción idempotente con
    // `CreatedAtRoute` no reintroduzca el fallo creyéndolo cubierto. Hacerlo bien pide
    // memorizar desde un IAsyncResultFilter, guardando clave y token en
    // `HttpContext.Items` — anotado en planning/16 §16.6.
    Dictionary<string, string>? headers = null;

    await store.SaveAsync(
        key,
        acquisition.Fence,
        requestHash,
        new IdempotentResponse(status.Value, body, contentType, headers),
        options.ResponseTtl,
        CancellationToken.None);
  }

  /// <summary>
  /// Rechaza con <b>422</b> una clave reutilizada con otro cuerpo.
  /// </summary>
  /// <remarks>
  /// <para>
  /// Sin esto, reutilizar una <c>Idempotency-Key</c> con un cuerpo distinto reproducía la
  /// respuesta de la primera <b>en silencio</b>: el cliente pedía comprar 5 unidades,
  /// recibía un 200 con el resultado de haber comprado 1, y nada indicaba que su segunda
  /// petición no se había ejecutado. Un fallo silencioso en el mecanismo que existe
  /// precisamente para no cobrar de más.
  /// </para>
  /// <para>
  /// 422 y no 409: el request está bien formado y no choca con el estado de la base — lo
  /// que pasa es que <b>no se puede procesar</b> porque contradice a otro que el cliente
  /// mandó con la misma clave. Es la respuesta que fija el borrador de idempotencia de la
  /// IETF, y lo que hacen Stripe y compañía.
  /// </para>
  /// </remarks>
  private static bool MismatchedBody(
      ActionExecutingContext context, IdempotencyEntry entry, string requestHash)
  {
    // Huella vacía = no se pudo calcular (ver HashOf). No se inventa un conflicto.
    if (requestHash.Length == 0 || entry.RequestHash.Length == 0) return false;

    if (entry.RequestHash == requestHash) return false;

    context.Result = new UnprocessableEntityObjectResult(new ProblemDetails
    {
      Status = StatusCodes.Status422UnprocessableEntity,
      Title = "Idempotency key reused with a different body",
      Detail = $"The {HeaderName} was already used for a different request. Use a new key.",
      Extensions = { ["code"] = "idempotency_key_reuse" }
    });

    return true;
  }

  /// <summary>Huella estable del cuerpo de la petición.</summary>
  /// <remarks>
  /// <para>
  /// Se calcula sobre los <b>argumentos ya enlazados</b> y no sobre el flujo crudo: para
  /// cuando corre un filtro de acción, el model binder ya consumió el cuerpo y releerlo
  /// exigiría un middleware que active <c>EnableBuffering</c> en <b>todas</b> las
  /// peticiones. Los argumentos son además una huella mejor: dos cuerpos que solo difieren
  /// en espacios o en el orden de los campos son la misma petición.
  /// </para>
  /// <para>
  /// Si algo no se puede serializar, devuelve cadena vacía y la comprobación se salta:
  /// una huella imposible de calcular no puede convertirse en un 422 para el cliente.
  /// </para>
  /// </remarks>
  private static string HashOf(ActionExecutingContext context)
  {
    try
    {
      var arguments = context.ActionArguments
          .Where(a => a.Value is not CancellationToken)
          .OrderBy(a => a.Key, StringComparer.Ordinal)
          .ToDictionary(a => a.Key, a => a.Value);

      var json = JsonSerializer.SerializeToUtf8Bytes(arguments, SerializerOptions);

      return Convert.ToHexString(SHA256.HashData(json));
    }
    catch (Exception)
    {
      return string.Empty;
    }
  }

  /// <summary>Reproduce una respuesta memorizada, cabeceras incluidas.</summary>
  private static void Replay(ActionExecutingContext context, IdempotentResponse cached)
  {
    var response = context.HttpContext.Response;

    response.Headers[ReplayedHeader] = "true";

    if (cached.Headers is not null)
      foreach (var (name, value) in cached.Headers)
        response.Headers[name] = value;

    context.Result = new ContentResult
    {
      StatusCode = cached.StatusCode,
      Content = cached.Body,
      // Sin ContentType si no lo había: un 204 no debe llevar Content-Type.
      ContentType = cached.ContentType
    };
  }
}
