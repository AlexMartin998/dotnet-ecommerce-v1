using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Administración de los mensajes que agotaron sus reintentos: ver cuántos hay y
/// devolverlos a la cola principal.
/// </summary>
/// <remarks>
/// <para>
/// Es la salida de la DLQ. Antes de esto, un comprobante que agotaba sus intentos dejaba
/// la orden en <c>failed</c> —correcto, el cliente deja de esperar— pero **reemitirlo
/// exigía entrar a la consola del broker**. Una cola de la que no se sale no es una red de
/// seguridad, es un vertedero.
/// </para>
/// <para>
/// ⚠️ <b>Vive en <c>Shared/Messaging</c> y no en un slice</b> porque no es de ningún
/// dominio: opera sobre el mecanismo. Y no nombra ningún tipo de <c>Features/</c>, que es
/// lo que la regla de dirección de dependencias prohíbe.
/// </para>
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/dead-letter")]
[Produces("application/json")]
// Mover mensajes en un broker es una operación de operador, no de usuario.
[Authorize(Roles = Roles.Admin)]
public class DeadLetterController : ControllerBase
{
    private readonly IDeadLetterAdmin _admin;

    public DeadLetterController(IDeadLetterAdmin admin)
    {
        _admin = admin;
    }

    /// <summary>Cuántos mensajes hay parados en cada cola de dead-letters.</summary>
    /// <remarks>
    /// Devuelve <b>recuentos, no contenido</b>: para decidir si hay que reemitir basta con
    /// saber cuántos hay, y volcar payloads sería una fuga esperando a que alguien publique
    /// un evento más rico que los de hoy.
    /// </remarks>
    [HttpGet(Name = "GetDeadLetters")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    // Sin broker no es un error del cliente ni un fallo nuestro: es que la feature no está.
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<IReadOnlyList<DeadLetterStatus>>> GetDeadLetters(
        CancellationToken ct)
        => Ok(await _admin.GetStatusAsync(ct));

    /// <summary>Devuelve mensajes muertos a su cola principal, con el presupuesto a cero.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ El <paramref name="queue"/> es el nombre de la cola <b>principal</b>, y se valida
    /// contra las colas que este servicio consume. Una cola desconocida devuelve
    /// <b>404</b>: la lista de suscripciones registradas es una allowlist por construcción,
    /// y sin ella el endpoint movería mensajes de cualquier cola del broker — que es
    /// compartido con otros proyectos.
    /// </para>
    /// <para>
    /// Es <b>idempotente en el efecto</b>, no en la operación: reemitir dos veces publica
    /// dos veces, pero el inbox deduplica por <c>MessageId</c> y el trabajo se hace una.
    /// </para>
    /// </remarks>
    /// <param name="queue">Cola principal cuya dead-letter se vacía.</param>
    /// <param name="max">Tope de mensajes a mover (1..500).</param>
    /// <param name="ct">Token de cancelación.</param>
    [HttpPost("{queue}/replay", Name = "ReplayDeadLetters")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<object>> ReplayDeadLetters(
        string queue, [FromQuery] int max, CancellationToken ct)
    {
        // El tope tiene un techo: sin él, un `max` enorme deja la petición HTTP moviendo
        // mensajes de uno en uno durante minutos. Quien necesite más, llama otra vez.
        if (max is < 1 or > 500)
            return ValidationProblem("max must be between 1 and 500.");

        var moved = await _admin.ReplayAsync(queue, max, ct);

        return Ok(new { queue, replayed = moved });
    }
}
