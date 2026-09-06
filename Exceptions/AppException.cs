using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>
/// Base de los errores de negocio: llevan el código y el estado HTTP que
/// <c>GlobalExceptionHandler</c> traduce a <c>ProblemDetails</c>.
/// </summary>
public abstract class AppException : Exception
{

  protected AppException(string code, string message, HttpStatusCode status)
      : base(message) { Code = code; Status = status; }

  /// <summary>Estado HTTP con el que se responde.</summary>
  public HttpStatusCode Status { get; }

  /// <summary>Identificador estable del error, para que el cliente pueda ramificar.</summary>
  public string Code { get; }

}
