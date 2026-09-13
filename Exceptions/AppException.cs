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

  /// <summary>Datos del error que el cliente necesita además del código (p. ej. qué <c>sku</c>).</summary>
  /// <remarks>
  /// Viajan como extensiones del <c>ProblemDetails</c>, al lado de <c>code</c>. Sin esto el
  /// cliente tendría que sacar el dato del mensaje, que es texto y no contrato.
  /// </remarks>
  public IDictionary<string, object?> Extensions { get; } = new Dictionary<string, object?>();

}
