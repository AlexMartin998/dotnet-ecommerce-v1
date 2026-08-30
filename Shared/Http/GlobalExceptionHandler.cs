using System.Net;
using ApiEcommerce.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using ApiEcommerce.Features.Catalog.Models;

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
    var (status, code, title) = Map(exception);

    if ((int)status >= 500)
      logger.LogError(exception, "Unhandled exception on {Method} {Path}",
          httpContext.Request.Method, httpContext.Request.Path);
    else
      logger.LogWarning("{Code} on {Method} {Path}: {Message}",
          code, httpContext.Request.Method, httpContext.Request.Path, exception.Message);

    httpContext.Response.StatusCode = (int)status;

    var problem = new ProblemDetails
    {
      Status = (int)status,
      Title = title,
      // nunca se filtra el mensaje real de una excepción no controlada
      Detail = (int)status >= 500 ? "An unexpected error occurred." : exception.Message,
      Type = $"https://httpstatuses.io/{(int)status}",
      Instance = $"{httpContext.Request.Method} {httpContext.Request.Path}",
      Extensions =
      {
        ["code"] = code,
        ["traceId"] = httpContext.TraceIdentifier
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
