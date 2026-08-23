using System.Net;
using ApiEcommerce.Exceptions;
using Microsoft.AspNetCore.Diagnostics;
using Microsoft.AspNetCore.Mvc;

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

    // BCL: red de seguridad mientras quede código viejo sin migrar.
    // NO es una alternativa válida en código nuevo: los servicios lanzan AppException.
    KeyNotFoundException => (HttpStatusCode.NotFound, "not_found", "Not found"),
    InvalidOperationException => (HttpStatusCode.Conflict, "conflict", "Conflict"),
    ArgumentException => (HttpStatusCode.BadRequest, "bad_request", "Bad request"),
    UnauthorizedAccessException => (HttpStatusCode.Unauthorized, "unauthorized", "Unauthorized"),
    OperationCanceledException => ((HttpStatusCode)499, "client_closed_request", "Client closed request"),

    _ => (HttpStatusCode.InternalServerError, "internal_error", "Internal server error")
  };
}
