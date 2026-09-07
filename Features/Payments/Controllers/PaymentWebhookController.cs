using ApiEcommerce.Exceptions;
using ApiEcommerce.Features.Payments.Models;
using ApiEcommerce.Features.Payments.Service;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;

namespace ApiEcommerce.Features.Payments.Controllers;


/// <summary>El extremo que escucha a las pasarelas.</summary>
/// <remarks>
/// Separado de <c>PaymentController</c> a propósito: es lo único anónimo del contexto, y
/// mezclarlo con las acciones autenticadas invita a que un día alguien mueva el
/// <c>[Authorize]</c> de sitio y lo abra sin querer.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/payment/webhook")]
[Produces("application/json")]
public class PaymentWebhookController : ControllerBase
{
    /// <summary>Tope del cuerpo. Un webhook legítimo no llega ni de lejos.</summary>
    private const int MaxPayloadBytes = 256 * 1024;

    private readonly IPaymentService _service;
    private readonly ILogger<PaymentWebhookController> _logger;

    public PaymentWebhookController(
        IPaymentService service, ILogger<PaymentWebhookController> logger)
    {
        _service = service;
        _logger = logger;
    }

    /// <summary>Recibe un evento de una pasarela.</summary>
    /// <remarks>
    /// <c>[AllowAnonymous]</c> porque <b>la firma ES la autenticación</b>: no hay usuario
    /// detrás de una llamada de Stripe. Y <c>[DisableRateLimiting]</c> porque las pasarelas
    /// reintentan en ráfaga desde unas pocas IPs: con el limitador global, un pico de
    /// reintentos se auto-bloquearía y perderíamos cobros de vista.
    /// </remarks>
    [AllowAnonymous]
    [DisableRateLimiting]
    [HttpPost("{provider}", Name = "PaymentWebhook")]
    [Consumes("application/json")]
    [RequestSizeLimit(MaxPayloadBytes)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<IActionResult> Receive(string provider, CancellationToken ct)
    {
        if (!Enum.TryParse<PaymentProvider>(provider, ignoreCase: true, out var parsed))
        {
            // 400 y no 404: la URL existe, lo que no existe es ese proveedor. Y no se
            // registra el cuerpo, que lo manda cualquiera.
            _logger.LogWarning("Webhook for unknown provider {Provider}", provider);

            throw new BadOperationAppException($"Unknown payment provider '{provider}'.");
        }

        // Cuerpo CRUDO: la firma es un HMAC sobre estos bytes exactos, y dejar que MVC
        // deserialice y reserialice el JSON la rompe.
        var payload = await ReadBodyAsync(ct);

        await _service.HandleWebhookAsync(
            parsed, payload, Request.Headers["Stripe-Signature"].FirstOrDefault(), ct);

        // 200 aunque el evento no nos diga nada: un error haría que la pasarela lo
        // reintentara durante días.
        return Ok(new { received = true });
    }

    private async Task<string> ReadBodyAsync(CancellationToken ct)
    {
        using var reader = new StreamReader(Request.Body);

        return await reader.ReadToEndAsync(ct);
    }
}
