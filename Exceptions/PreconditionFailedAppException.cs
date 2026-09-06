using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>
/// 412 — el cliente puso una condición (<c>If-Match</c>) y el recurso ya no está en ese
/// estado.
/// </summary>
/// <remarks>
/// <b>412 y no 409</b>, aunque las dos hablen de conflicto. El 409 dice "tu petición
/// choca con el estado actual"; el 412 dice "la <i>precondición</i> que TÚ pusiste no se
/// cumple". La diferencia es accionable para el cliente: ante un 412 sabe que debe releer
/// el recurso, quedarse con el <c>ETag</c> nuevo y decidir si su cambio sigue teniendo
/// sentido — que es exactamente lo que hay que hacer ante un <i>lost update</i>.
/// </remarks>
public sealed class PreconditionFailedAppException(
    string message = "The resource was modified by someone else. Re-read it and retry.")
    : AppException("precondition_failed", message, HttpStatusCode.PreconditionFailed)
{
}
