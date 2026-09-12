using System.ComponentModel.DataAnnotations;
using ApiEcommerce.Features.Ordering.Dtos;
using ApiEcommerce.Features.Ordering.Service;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Shared.Paging;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Features.Ordering.Controllers;


[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]   // api/v1/order
[Produces("application/json")]
// El [Authorize] de clase lleva el requisito débil: varios [Authorize] se combinan (AND).
[Authorize]
public class OrderController : ControllerBase
{
    private readonly IOrderService _service;

    public OrderController(IOrderService service)
    {
        _service = service;
    }

    /// <summary>Cierra una compra y crea la orden.</summary>
    /// <remarks>
    /// La respuesta no espera al comprobante: la orden vuelve con
    /// <c>receiptStatus: "pending"</c> y el PDF se genera aparte.
    /// </remarks>
    [HttpPost(Name = "PlaceOrder")]
    // Corta la petición antes de leerla entera; validar después de recibir no protege.
    [RequestSizeLimit(64 * 1024)]
    // Atajo, no la garantía: quien impide la doble compra es OrderService.
    // Sin [Transactional]: la transacción la abre el servicio, y el atributo aquí lanzaría.
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<OrderDto>> PlaceOrder(
        [FromBody] PlaceOrderDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // El controller traduce protocolo a dominio: la cabecera se vuelve una intención.
        var intent = HttpContext.CommandIntentFor("ordering.place-order");

        // SKU inexistente o sin stock -> 409 ; clave reusada con otro cuerpo -> 422
        var outcome = await _service.PlaceAsync(
            dto, intent, User.GetRequiredUserId(), User.GetEmail(), ct);

        if (outcome.WasReplayed)
            Response.Headers[IdempotentAttribute.ReplayedHeader] = "true";

        // 201 con cuerpo: el cliente necesita número y totales para pintar la confirmación.
        return CreatedAtRoute(
            "GetOrder",
            new { version = HttpContext.ApiVersionValue(), id = outcome.Result.Id },
            outcome.Result);
    }

    /// <summary>Una orden del comprador autenticado.</summary>
    [HttpGet("{id:int}", Name = "GetOrder")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<OrderDto>> GetOrder(int id, CancellationToken ct)
        // La orden de otro devuelve 404, no 403: ver IOrderService.
        => Ok(await _service.GetForBuyerAsync(id, User.GetRequiredUserId(), ct));

    /// <summary>Las órdenes del comprador, de la más reciente a la más antigua.</summary>
    [HttpGet("paged", Name = "GetOrdersPaged")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetOrdersPaged(
        [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        return Ok(await _service.GetPagedForBuyerAsync(query, User.GetRequiredUserId(), ct));
    }

    /// <summary>Todas las órdenes, de cualquier comprador. Solo administración.</summary>
    /// <remarks>
    /// Endpoint aparte y no un parámetro de <c>/paged</c>: quien puede ver las compras de
    /// terceros no es el mismo que quien puede ver las suyas, y una ruta por autorización
    /// hace imposible que un fallo de filtrado convierta un listado propio en uno global.
    /// </remarks>
    [Authorize(Roles = Roles.Admin)]
    [HttpGet("all", Name = "GetAllOrders")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<OrderDto>>> GetAllOrders(
        [FromQuery] PageQuery query,
        [FromQuery][StringLength(50)] string? number,
        CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        return Ok(await _service.GetPagedForAdminAsync(query, number, ct));
    }

    /// <summary>Contadores de órdenes por estado. Solo administración.</summary>
    /// <remarks>
    /// Cada contexto publica los suyos: el panel hace tres llamadas en paralelo en vez de
    /// obligar a un slice a conocer el catálogo y los usuarios.
    /// </remarks>
    [Authorize(Roles = Roles.Admin)]
    [HttpGet("stats", Name = "GetOrderStats")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<OrderStatsDto>> GetOrderStats(CancellationToken ct)
        => Ok(await _service.GetStatsAsync(ct));

    /// <summary>Mueve una orden por su ciclo de entrega. Solo administración.</summary>
    /// <remarks>
    /// Sin <c>Idempotency-Key</c> a propósito: la transición es un UPDATE condicional, así
    /// que repetirla no repite nada y devuelve 204 igual.
    /// </remarks>
    [Authorize(Roles = Roles.Admin)]
    [HttpPatch("{id:int}/status", Name = "UpdateOrderStatus")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateOrderStatus(
        int id, [FromBody] UpdateOrderStatusDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        await _service.AdvanceAsync(id, dto, ct);

        return NoContent();
    }

    /// <summary>Descarga el comprobante en PDF.</summary>
    /// <remarks>
    /// El PDF se sirve por aquí y no como estático, con tres barreras: vive fuera de
    /// <c>wwwroot/</c>, su clave es aleatoria y esta acción comprueba de quién es la orden.
    /// Devuelve un <c>Stream</c> para no cargar el documento entero en memoria.
    /// </remarks>
    [HttpGet("{id:int}/receipt", Name = "GetOrderReceipt")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    // 409 y no 404: el comprobante existirá, y un 404 diría al cliente que deje de pedirlo.
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetOrderReceipt(int id, CancellationToken ct)
    {
        var receipt = await _service.GetReceiptAsync(id, User.GetRequiredUserId(), ct);

        // El documento lleva datos personales: `Authorization` ya descarta las caches
        // compartidas, pero no el disco del navegador, que conserva el PDF tras cerrar sesión.
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";

        // Sin `enableRangeProcessing`: obligaría al almacén a soportar lecturas parciales.
        return File(receipt.Stream, receipt.ContentType, receipt.FileName);
    }
}
