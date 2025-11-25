using System.Net;

namespace ApiEcommerce.Exceptions;


public sealed class BadOperationAppException(string message) : AppException("bad_request", message, HttpStatusCode.BadRequest)
{
}
