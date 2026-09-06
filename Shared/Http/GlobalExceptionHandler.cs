using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Handler global de errores: el equivalente a <c>@ControllerAdvice</c> +
/// <c>@ExceptionHandler</c> de Spring. Traduce toda excepción que escape de un
/// controller a una respuesta <c>ProblemDetails</c> (RFC 7807) uniforme.
/// Gracias a él, <b>ningún controller vuelve a escribir try/catch de negocio</b>.
/// </summary>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService)
  : IExceptionHandler
{
  public async ValueTask<bool> TryHandleAsync(
      HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
  {
    // Nota: aquí NO se comprueba si el cliente colgó. Se hace en ClientAbortMiddleware,
    // que va por debajo de UseExceptionHandler, porque el middleware de diagnóstico del
    // framework escribe su "unhandled exception" a nivel Error ANTES de llamar a este
    // handler: comprobarlo aquí llega tarde, la línea de Error ya está en el log.

    var (status, code, title) = Map(exception);

    if ((int)status >= 500)
      logger.LogError(exception, "Unhandled exception on {Method} {Path}",
          httpContext.Request.Method, httpContext.Request.Path);
    else
      logger.LogWarning("{Code} on {Method} {Path}: {Message}",
          code, httpContext.Request.Method, httpContext.Request.Path, exception.Message);

    httpContext.Response.StatusCode = (int)status;

    // Un 503 sin `Retry-After` obliga al cliente a adivinar si puede reintentar y cuándo.
    // Es la misma idea que la cabecera `transient-error` de Adyen: no basta con rechazar,
    // hay que decir si el rechazo es transitorio.
    if (status == HttpStatusCode.ServiceUnavailable)
      httpContext.Response.Headers.RetryAfter = "1";

    var problem = new ProblemDetails
    {
      Status = (int)status,
      Title = title,
      // Nunca se filtra el mensaje real de una excepción NO CONTROLADA. Pero un 5xx que
      // sí está mapeado —el 503 por timeout de base— trae un texto que escribimos
      // nosotros, es accionable ("reintenta") y no revela nada del servidor: censurarlo
      // por el simple hecho de ser 5xx dejaba al cliente sin saber si podía reintentar.
      // El corte es "¿lo mapeamos nosotros?", no "¿es 5xx?".
      Detail = code switch
      {
        // Excepción NO controlada: nunca se filtra su mensaje real.
        "internal_error" => "An unexpected error occurred.",
        // 5xx que SÍ mapeamos (el 503 por timeout de base): el texto lo escribimos
        // nosotros, es accionable ("reintenta") y no revela nada del servidor.
        // Censurarlo por el simple hecho de ser 5xx dejaba al cliente sin saber si podía
        // reintentar. El corte es "¿lo mapeamos nosotros?", no "¿es 5xx?".
        _ when (int)status >= 500 => title,
        // 4xx: el mensaje de la excepción de dominio es EL útil ("Insufficient stock for
        // SKU 'X'"). Es parte del contrato de la API, no una fuga.
        _ => exception.Message
      },
      Type = $"https://httpstatuses.io/{(int)status}",
      Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
      Extensions =
      {
        ["code"] = code,
        // El MISMO id que viaja en la cabecera X-Correlation-Id, que es el que el cliente
        // ve y el que va a citar al abrir el ticket. Antes se ponía `TraceIdentifier`, que
        // además el escritor de ProblemDetails del framework machaca con `Activity.Id`:
        // el cuerpo y la cabecera llevaban DOS ids distintos para la misma petición, que
        // es justo la confusión que la correlación viene a quitar.
        ["correlationId"] = httpContext.Items[CorrelationIdMiddleware.HeaderName] as string
                            ?? httpContext.TraceIdentifier
      }
    };

    // 422: mismo formato `errors` que produce ValidationProblem(ModelState)
    if (exception is ValidationAppException validation)
      problem.Extensions["errors"] = validation.Errors;

    return await problemDetailsService.TryWriteAsync(new ProblemDetailsContext
    {
      HttpContext = httpContext,
      Exception = exception,
      ProblemDetails = problem
    });
  }

  private static (HttpStatusCode Status, string Code, string Title) Map(Exception ex) => ex switch
  {
    // dominio: la propia excepción trae su código y su HTTP
    AppException app => (app.Status, app.Code, app.Code.Replace('_', ' ')),

    // ---- carreras que la comprobación previa no puede evitar -----------------
    // Estas NO son "código viejo sin migrar": son el caso en el que dos peticiones
    // simultáneas pasan las dos la validación y es la BASE la que arbitra. Sin
    // traducirlas, arreglar la carrera (índice único, RowVersion) empeora la
    // respuesta: el 409 correcto se convierte en un 500.

    // Otro request modificó la fila entre el SELECT y el UPDATE (Product.RowVersion).
    DbUpdateConcurrencyException => (HttpStatusCode.Conflict, "concurrency_conflict",
        "The resource was modified by another request. Retry the operation."),

    // Se busca el SqlException RECORRIENDO la cadena de InnerException en vez de
    // hacer pattern matching sobre una forma concreta de anidamiento. El anidamiento
    // NO es estable:
    //   · SaveChangesAsync   -> DbUpdateException { SqlException }
    //   · ExecuteUpdateAsync -> SqlException DESNUDO (no pasa por SaveChanges)
    //   · con EnableRetryOnFailure agotado -> RetryLimitExceededException { ... }
    // La versión anterior solo acertaba el primer caso, así que un choque en
    // TryDecrementStockAsync —la sentencia con más contención del sistema— salía 500.
    _ when FindSqlException(ex) is { Number: 2601 or 2627 }
        => (HttpStatusCode.Conflict, "conflict", "The value already exists."),

    // 1205: deadlock. Es reintentable, y el cliente debe saberlo.
    _ when FindSqlException(ex) is { Number: 1205 }
        => (HttpStatusCode.Conflict, "deadlock", "Deadlock detected. Retry the operation."),

    // 547: violación de clave foránea (la fila relacionada se borró entre la
    // validación y la escritura).
    _ when FindSqlException(ex) is { Number: 547 }
        => (HttpStatusCode.Conflict, "fk_violation", "A related resource constraint was violated."),

    // -2: timeout de comando (el Win32 258 que se ve dentro es WAIT_TIMEOUT). No es un
    // bug nuestro ni una petición mal formada: es que la base no llegó a tiempo, casi
    // siempre por contención o por saturación. 503 y no 500 porque **es reintentable**, y
    // el cliente necesita saberlo: un 500 le dice "no lo vuelvas a intentar así".
    // Medido en las pruebas de carga: 83 de estos salían como error interno.
    _ when FindSqlException(ex) is { Number: -2 }
        => (HttpStatusCode.ServiceUnavailable, "database_timeout",
            "The database did not respond in time. Retry the operation."),

    // BCL: red de seguridad mientras quede código viejo sin migrar.
    // NO es una alternativa válida en código nuevo: los servicios lanzan AppException.
    KeyNotFoundException => (HttpStatusCode.NotFound, "not_found", "Not found"),
    ArgumentException => (HttpStatusCode.BadRequest, "bad_request", "Bad request"),

    // InvalidOperationException NO está aquí a propósito. EF Core la usa para errores
    // de PROGRAMACIÓN ("the instance of entity type X cannot be tracked because...",
    // "the configured execution strategy does not support user-initiated
    // transactions"), no de negocio. Mapearla a 409 daba el código equivocado Y
    // filtraba mensajes internos del ORM al cliente, porque Detail solo se censura a
    // partir de 500. Que caiga a 500, que es lo que realmente es.

    // El broker no está. No es culpa de quien llama ni un fallo nuestro: es una
    // dependencia caída, y **es reintentable** — el handler añade `Retry-After` a todo 503.
    // Solo llega aquí desde los endpoints de administración de mensajería; el outbox la
    // trata por su cuenta y nunca la deja escapar a HTTP.
    Messaging.BrokerUnavailableException => (HttpStatusCode.ServiceUnavailable,
        "broker_unavailable", "Messaging is unavailable"),

    UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "unauthorized", "Unauthorized"),
    OperationCanceledException => ((HttpStatusCode)499, "client_closed_request", "Client closed request"),

    _ => (HttpStatusCode.InternalServerError, "internal_error", "Internal server error")
  };

  /// <summary>
  /// Recorre la cadena de <c>InnerException</c> buscando un <see cref="SqlException"/>.
  /// </summary>
  private static SqlException? FindSqlException(Exception? exception)
  {
    for (var current = exception; current is not null; current = current.InnerException)
      if (current is SqlException sql) return sql;

    return null;
  }
}
