using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>
/// 422 — la misma clave de idempotencia se reusó con una petición distinta.
/// </summary>
/// <remarks>
/// 422 y no 409 porque la petición está bien formada y no choca con la base: contradice a
/// otra con la misma clave, que es lo que fija el borrador de idempotencia de la IETF. Es
/// una excepción de dominio porque la comprobación vive dentro de la transacción.
/// </remarks>
public sealed class IdempotencyConflictAppException(string message)
    : AppException("idempotency_key_reuse", message, HttpStatusCode.UnprocessableEntity)
{
}
