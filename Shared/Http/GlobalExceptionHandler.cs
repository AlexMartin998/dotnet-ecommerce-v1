using System.Net;
using ApiEcommerce.Exceptions;
using ApiEcommerce.Shared.Observability;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;

namespace ApiEcommerce.Shared.Http;


/// <summary>
/// Handler global de errores: traduce toda excepción que escape de un controller a un
/// <c>ProblemDetails</c> (RFC 7807) uniforme.
/// </summary>
/// <remarks>
/// Equivale al <c>@ControllerAdvice</c> de Spring, y es lo que permite que ningún
/// controller escriba try/catch de negocio.
/// </remarks>
public sealed class GlobalExceptionHandler(
    ILogger<GlobalExceptionHandler> logger,
    IProblemDetailsService problemDetailsService)
  : IExceptionHandler
{
  /// <summary>Registra el fallo y escribe la respuesta <c>ProblemDetails</c>.</summary>
  public async ValueTask<bool> TryHandleAsync(
      HttpContext httpContext, Exception exception, CancellationToken cancellationToken)
  {
    // Que el cliente haya colgado se comprueba en ClientAbortMiddleware, no aquí: el
    // middleware del framework ya escribió su línea de Error antes de llamar a este handler.

    var (status, code, title) = Map(exception);

    if ((int)status >= 500)
      logger.LogError(exception, "Unhandled exception on {Method} {Path}",
          httpContext.Request.Method, httpContext.Request.Path);
    else
      logger.LogWarning("{Code} on {Method} {Path}: {Message}",
          code, httpContext.Request.Method, httpContext.Request.Path, exception.Message);

    httpContext.Response.StatusCode = (int)status;

    // Un 503 sin `Retry-After` obliga al cliente a adivinar si puede reintentar y cuándo.
    if (status == HttpStatusCode.ServiceUnavailable)
      httpContext.Response.Headers.RetryAfter = "1";

    var problem = new ProblemDetails
    {
      Status = (int)status,
      Title = title,
      // El corte para censurar el mensaje es «¿lo mapeamos nosotros?», no «¿es 5xx?».
      Detail = code switch
      {
        // Excepción no controlada: nunca se filtra su mensaje real.
        "internal_error" => "An unexpected error occurred.",
        // 5xx mapeado (el 503 por timeout de base): el texto es nuestro, accionable y no
        // revela nada del servidor.
        _ when (int)status >= 500 => title,
        // 4xx: el mensaje de dominio es el útil y es parte del contrato, no una fuga.
        _ => exception.Message
      },
      Type = $"https://httpstatuses.io/{(int)status}",
      Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
      Extensions =
      {
        ["code"] = code,
        // El mismo id que viaja en X-Correlation-Id: con `TraceIdentifier`, el cuerpo y la
        // cabecera llevaban dos ids distintos para la misma petición.
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
    // Dos peticiones simultáneas pasan las dos la validación y arbitra la base. Sin
    // traducirlas, arreglar la carrera convertiría el 409 correcto en un 500.

    // Otro request modificó la fila entre el SELECT y el UPDATE (Product.RowVersion).
    DbUpdateConcurrencyException => (HttpStatusCode.Conflict, "concurrency_conflict",
        "The resource was modified by another request. Retry the operation."),

    // Se recorre la cadena de InnerException porque el anidamiento no es estable:
    // SaveChangesAsync envuelve el SqlException, ExecuteUpdateAsync lo lanza desnudo y
    // EnableRetryOnFailure agotado añade otra capa.
    _ when FindSqlException(ex) is { Number: 2601 or 2627 }
        => (HttpStatusCode.Conflict, "conflict", "The value already exists."),

    // 1205: deadlock. Es reintentable, y el cliente debe saberlo.
    _ when FindSqlException(ex) is { Number: 1205 }
        => (HttpStatusCode.Conflict, "deadlock", "Deadlock detected. Retry the operation."),

    // 547: violación de clave foránea (la fila relacionada se borró entre la
    // validación y la escritura).
    _ when FindSqlException(ex) is { Number: 547 }
        => (HttpStatusCode.Conflict, "fk_violation", "A related resource constraint was violated."),

    // -2: timeout de comando. 503 y no 500 porque es reintentable y el cliente necesita
    // saberlo.
    _ when FindSqlException(ex) is { Number: -2 }
        => (HttpStatusCode.ServiceUnavailable, "database_timeout",
            "The database did not respond in time. Retry the operation."),

    // BCL: red de seguridad mientras quede código viejo sin migrar; en código nuevo los
    // servicios lanzan AppException.
    KeyNotFoundException => (HttpStatusCode.NotFound, "not_found", "Not found"),
    ArgumentException => (HttpStatusCode.BadRequest, "bad_request", "Bad request"),

    // InvalidOperationException no está aquí a propósito: EF la usa para errores de
    // programación, así que mapearla daba el código equivocado y filtraba mensajes del ORM.

    // El broker no está: dependencia caída y reintentable. Solo llega aquí desde los
    // endpoints de administración de mensajería.
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
