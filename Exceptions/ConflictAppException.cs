using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>409 — la petición choca con el estado actual del recurso (duplicados, etc.).</summary>
public sealed class ConflictAppException(string message) : AppException("conflict", message, HttpStatusCode.Conflict)
{
}
