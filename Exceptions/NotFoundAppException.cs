using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>404 — no existe la entidad pedida con esa clave.</summary>
public class NotFoundAppException(string entity, object key) : AppException("not_found", $"{entity} with key '{key}' was not found.", HttpStatusCode.NotFound)
{
}
