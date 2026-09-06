using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Messaging.RabbitMq;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Shared.Messaging;


/// <summary>
/// Administración de los mensajes que agotaron sus reintentos: ver cuántos hay y devolverlos a
/// la cola principal.
/// </summary>
/// <remarks>
/// Es la salida de la DLQ; sin ella, reemitir exigía entrar a la consola del broker. Vive en
/// <c>Shared/Messaging</c> y no en un slice porque opera sobre el mecanismo y no nombra ningún
/// tipo de <c>Features/</c>.
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
    /// Devuelve recuentos y no contenido: volcar payloads sería una fuga, y para decidir si hay
    /// que reemitir basta con saber cuántos hay.
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
    /// El <paramref name="queue"/> es la cola principal y se valida contra las suscripciones
    /// registradas, que son la allowlist; una desconocida da 404. Reemitir dos veces publica
    /// dos veces, pero el inbox deduplica por <c>MessageId</c> y el trabajo se hace una.
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
        // Con techo: sin él, un `max` enorme deja la petición moviendo mensajes durante minutos.
        if (max is < 1 or > 500)
            return ValidationProblem("max must be between 1 and 500.");

        var moved = await _admin.ReplayAsync(queue, max, ct);

        return Ok(new { queue, replayed = moved });
    }
}
