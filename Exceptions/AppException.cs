using System.Net;

namespace ApiEcommerce.Exceptions;


public abstract class AppException : Exception
{

  protected AppException(string code, string message, HttpStatusCode status)
      : base(message) { Code = code; Status = status; }

  public HttpStatusCode Status { get; }

  public string Code { get; }

}
