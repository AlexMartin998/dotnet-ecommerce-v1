using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>
/// 412 — el cliente puso una condición (<c>If-Match</c>) y el recurso ya no está en ese
/// estado.
/// </summary>
/// <remarks>
/// 412 y no 409: el 409 dice "tu petición choca con el estado actual", el 412 dice "la
/// precondición que pusiste no se cumple". Ante un 412 el cliente sabe que debe releer el
/// recurso, quedarse con el <c>ETag</c> nuevo y decidir si su cambio sigue valiendo.
/// </remarks>
public sealed class PreconditionFailedAppException(
    string message = "The resource was modified by someone else. Re-read it and retry.")
    : AppException("precondition_failed", message, HttpStatusCode.PreconditionFailed)
{
}
