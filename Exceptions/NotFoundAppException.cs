using System.Net;

namespace ApiEcommerce.Exceptions;


public class NotFoundAppException(string entity, object key) : AppException("not_found", $"{entity} with key '{key}' was not found.", HttpStatusCode.NotFound)
{
}
