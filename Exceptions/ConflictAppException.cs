using System.Net;

namespace ApiEcommerce.Exceptions;


public sealed class ConflictAppException(string message) : AppException("conflict", message, HttpStatusCode.Conflict)
{
}
