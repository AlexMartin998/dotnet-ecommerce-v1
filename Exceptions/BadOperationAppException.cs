using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>400 — la petición no es una operación válida sobre el recurso.</summary>
public sealed class BadOperationAppException(string message) : AppException("bad_request", message, HttpStatusCode.BadRequest)
{
}
