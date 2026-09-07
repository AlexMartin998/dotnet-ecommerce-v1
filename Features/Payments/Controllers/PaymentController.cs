using System.ComponentModel.DataAnnotations;
using ApiEcommerce.Features.Payments.Dtos;
using ApiEcommerce.Features.Payments.Service;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Shared.Paging;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Features.Payments.Controllers;


[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]   // api/v1/payment
[Produces("application/json")]
// El requisito débil va en la clase: varios [Authorize] se combinan (AND).
[Authorize]
public class PaymentController : ControllerBase
{
    private readonly IPaymentService _service;

    public PaymentController(IPaymentService service)
    {
        _service = service;
    }

    /// <summary>Empieza a pagar una orden.</summary>
    /// <remarks>
    /// El 201 significa «la pasarela aceptó el intento», NO «está cobrado»: lo que mueve el
    /// pago a <c>captured</c> es el webhook firmado. El <c>clientSecret</c> es lo que el
    /// front necesita para confirmarlo, y no se guarda en ningún sitio.
    /// </remarks>
    [HttpPost(Name = "StartPayment")]
    [RequestSizeLimit(8 * 1024)]
    // Atajo, no la garantía: quien impide el doble cobro es PaymentService.
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    [ProducesResponseType(StatusCodes.Status503ServiceUnavailable)]
    public async Task<ActionResult<PaymentDto>> StartPayment(
        [FromBody] StartPaymentDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var intent = HttpContext.CommandIntentFor("payments.start-payment");

        var outcome = await _service.StartAsync(
            dto, intent, User.GetRequiredUserId(), User.GetEmail(), ct);

        if (outcome.WasReplayed)
            Response.Headers[IdempotentAttribute.ReplayedHeader] = "true";

        return CreatedAtRoute(
            "GetPayment",
            new { version = HttpContext.ApiVersionValue(), id = outcome.Result.Id },
            outcome.Result);
    }

    /// <summary>Un pago del comprador autenticado.</summary>
    [HttpGet("{id:int}", Name = "GetPayment")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<PaymentDto>> GetPayment(int id, CancellationToken ct)
        // El pago de otro devuelve 404, no 403.
        => Ok(await _service.GetForBuyerAsync(id, User.GetRequiredUserId(), ct));

    /// <summary>Los pagos del comprador, del más reciente al más antiguo.</summary>
    [HttpGet("paged", Name = "GetPaymentsPaged")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<PaymentDto>>> GetPaymentsPaged(
        [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        return Ok(await _service.GetPagedForBuyerAsync(query, User.GetRequiredUserId(), ct));
    }

    /// <summary>Todos los pagos, de cualquier comprador. Solo administración.</summary>
    /// <remarks>
    /// Ruta aparte y no un parámetro de <c>/paged</c>, igual que en órdenes: así la
    /// autorización no depende de que ningún filtro esté bien puesto.
    /// </remarks>
    [Authorize(Roles = Roles.Admin)]
    [HttpGet("all", Name = "GetAllPayments")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<PaymentDto>>> GetAllPayments(
        [FromQuery] PageQuery query,
        [FromQuery][StringLength(50)] string? reference,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        return Ok(await _service.GetPagedForAdminAsync(query, reference, ct));
    }
}
