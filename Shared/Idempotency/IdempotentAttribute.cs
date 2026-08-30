using System.Text.Json;
using ApiEcommerce.Shared.Auth;
using Microsoft.AspNetCore.Mvc;
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

  /// <summary>
  /// Ventana en la que se recuerda la RESPUESTA de una operación ya completada.
  /// </summary>
  private static readonly TimeSpan ResponseTtl = TimeSpan.FromHours(24);

  /// <summary>
  /// Vida de la RESERVA mientras la operación está en curso.
  /// </summary>
  /// <remarks>
  /// Corto a propósito, y distinto del anterior: si el proceso muere entre la reserva
  /// y el guardado (deploy, OOM-kill), con un TTL de 24 h la clave quedaba bloqueada
  /// un día entero devolviendo 409 por una operación que <b>nunca llegó a ejecutarse</b>,
  /// y el cliente no tenía forma de salir del bucle. Con 60 s se libera sola.
  /// </remarks>
  private static readonly TimeSpan ReservationTtl = TimeSpan.FromSeconds(60);

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

    var store = context.HttpContext.RequestServices.GetRequiredService<IIdempotencyStore>();

    // Se incluye el QueryString: `request.Path` no lo lleva, así que `POST /x?page=1`
    // y `POST /x?page=2` con la misma clave colisionaban. Y se normaliza a minúsculas
    // para que `/Product/buy` y `/product/buy` no generen dos claves.
    var route = $"{request.Method}:{request.Path.Value?.ToLowerInvariant()}{request.QueryString.Value}";
    var key = $"{user}:{route}:{header!}";

    // 1) ¿Ya terminó una petición con esta clave? Se reproduce su respuesta.
    if (await store.GetAsync(key, context.HttpContext.RequestAborted) is { } cached)
    {
      Replay(context, cached);
      return;
    }

    // 2) Reserva atómica. Si falla, hay otra petición idéntica EN CURSO ahora mismo.
    if (!await store.TryAcquireAsync(key, ReservationTtl, context.HttpContext.RequestAborted))
    {
      // Puede que la otra petición haya terminado entre el GET de arriba y esta
      // reserva. Se vuelve a mirar antes de dar 409: si ya hay respuesta, lo correcto
      // es reproducirla, no decirle al cliente que su operación sigue en curso.
      if (await store.GetAsync(key, context.HttpContext.RequestAborted) is { } justFinished)
      {
        Replay(context, justFinished);
        return;
      }

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
      await store.ReleaseAsync(key, CancellationToken.None);
      return;
    }

    string? body = null;
    string? contentType = null;

    if (executed.Result is ObjectResult { Value: not null } ok)
    {
      body = JsonSerializer.Serialize(ok.Value, SerializerOptions);
      contentType = "application/json";
    }

    // El Location de un 201 se captura aquí: sin él, el replay de un create devolvía
    // un 201 sin decir qué recurso se había creado.
    var headers = new Dictionary<string, string>();

    if (context.HttpContext.Response.Headers.TryGetValue("Location", out var location))
      headers["Location"] = location.ToString();

    await store.SaveAsync(
        key,
        new IdempotentResponse(status.Value, body, contentType, headers.Count > 0 ? headers : null),
        ResponseTtl,
        CancellationToken.None);
  }

  /// <summary>Reproduce una respuesta memorizada, cabeceras incluidas.</summary>
  private static void Replay(ActionExecutingContext context, IdempotentResponse cached)
  {
    var response = context.HttpContext.Response;

    response.Headers["Idempotency-Replayed"] = "true";

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
