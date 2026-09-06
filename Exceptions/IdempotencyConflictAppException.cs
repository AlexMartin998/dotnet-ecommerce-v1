using System.Net;

namespace ApiEcommerce.Exceptions;


/// <summary>
/// La misma intención de comando se reusó con una petición <b>distinta</b>. <b>422</b>.
/// </summary>
/// <remarks>
/// <para>
/// 422 y no 409: la petición está bien formada y no choca con el estado de la base — lo
/// que pasa es que <b>no se puede procesar</b> porque contradice a otra que el cliente
/// mandó con la misma clave. Es lo que fija el borrador de idempotencia de la IETF
/// (<c>draft-ietf-httpapi-idempotency-key-header</c>, un SHOULD desde la versión -07) y
/// lo que hace Stripe con su <c>idempotency_error</c>.
/// ⚠️ La industria <b>no</b> está unificada: Square devuelve 400 con
/// <c>IDEMPOTENCY_KEY_REUSED</c>. Se elige el del estándar y se documenta.
/// </para>
/// <para>
/// Sin esto, reutilizar una clave con otro cuerpo reproducía la respuesta de la primera
/// <b>en silencio</b>: el cliente pedía comprar 5 unidades, recibía un 200 con el
/// resultado de haber comprado 1, y nada indicaba que su segunda petición no se había
/// ejecutado. Un fallo silencioso en el mecanismo que existe precisamente para no cobrar
/// de más.
/// </para>
/// <para>
/// Es una excepción de <b>dominio</b> y no un resultado del filtro HTTP a propósito: la
/// comprobación vive ahora donde vive la garantía, o sea dentro de la transacción de
/// negocio, y por tanto tiene que poder expresarse sin conocer códigos de estado.
/// Quien traduce sigue siendo <c>GlobalExceptionHandler</c>.
/// </para>
/// </remarks>
public sealed class IdempotencyConflictAppException(string message)
    : AppException("idempotency_key_reuse", message, HttpStatusCode.UnprocessableEntity)
{
}
