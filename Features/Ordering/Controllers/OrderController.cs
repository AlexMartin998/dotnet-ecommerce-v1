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
// Ninguna acción es pública: una orden es de alguien. Y el [Authorize] de clase es el
// requisito DÉBIL (estar autenticado) porque varios [Authorize] se combinan (AND) y solo
// [AllowAnonymous] gana — poner aquí un rol lo exigiría también en las acciones.
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
    /// La respuesta <b>no espera al comprobante</b>: la orden vuelve con
    /// <c>receiptStatus: "pending"</c> y el PDF se genera aparte.
    /// </remarks>
    [HttpPost(Name = "PlaceOrder")]
    // Corta la petición ANTES de leerla entera. Sin esto, el único tope es el de Kestrel
    // (30 MB): un carrito de 300.000 líneas se enlaza y se aloja completo en memoria para
    // acabar en un 400 por el [MaxLength(50)]. Validar después de haber recibido no protege.
    [RequestSizeLimit(64 * 1024)]
    // ATAJO, no la garantía: responde un reintento sin tocar la base. Quien impide de
    // verdad la doble compra es OrderService, que escribe la marca del comando en la MISMA
    // transacción que la orden y el descuento de stock.
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

        // El controller traduce protocolo a dominio: la cabecera del cliente se convierte
        // aquí en una intención, y el servicio ya no sabe que existe HTTP.
        var intent = HttpContext.CommandIntentFor("ordering.place-order");

        // SKU inexistente o sin stock -> 409 ; clave reusada con otro cuerpo -> 422
        var outcome = await _service.PlaceAsync(
            dto, intent, User.GetRequiredUserId(), User.GetEmail(), ct);

        if (outcome.WasReplayed)
            Response.Headers[IdempotentAttribute.ReplayedHeader] = "true";

        // 201 CON cuerpo, a diferencia de POST /category: el cliente necesita el número de
        // orden y los totales para pintar la confirmación, y obligarle a un GET más
        // después de pagar es la peor petición extra posible.
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

    /// <summary>Descarga el comprobante en PDF.</summary>
    /// <remarks>
    /// <para>
    /// ⚠️ <b>El PDF se sirve por aquí y no como estático</b>, y hacen falta las tres
    /// barreras: el fichero vive <b>fuera de <c>wwwroot/</c></b> (no hay URL pública que
    /// adivinar), su clave es aleatoria (no se deduce de la orden ni del usuario) y esta
    /// acción comprueba <b>de quién es la orden</b>. Con solo las dos primeras, cualquier
    /// filtración de una clave sería una descarga; con solo la tercera,
    /// <c>UseStaticFiles</c> lo serviría sin pasar por autenticación.
    /// </para>
    /// <para>
    /// Devuelve un <c>Stream</c> y no un <c>byte[]</c>: MVC lo copia a la respuesta a
    /// trozos y libera el fichero al terminar, así que una descarga no se lleva el
    /// documento entero a memoria.
    /// </para>
    /// </remarks>
    [HttpGet("{id:int}/receipt", Name = "GetOrderReceipt")]
    [Produces("application/pdf")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    // 409 y no 404: el comprobante EXISTIRÁ, solo que todavía no. Un 404 le diría al
    // cliente que deje de pedirlo.
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> GetOrderReceipt(int id, CancellationToken ct)
    {
        var receipt = await _service.GetReceiptAsync(id, User.GetRequiredUserId(), ct);

        // El documento lleva nombre, dirección e importe. Las caches COMPARTIDAS ya quedan
        // fuera por la cabecera `Authorization` (RFC 9111 §3.5), pero el disco del
        // navegador no: sin esto, el PDF se queda cacheado en un equipo que puede ser
        // compartido, y sigue ahí después de cerrar sesión.
        Response.Headers.CacheControl = "no-store, no-cache, must-revalidate, private";

        // `enableRangeProcessing` no se activa: son unas decenas de KB y las peticiones
        // por rango obligarían al almacén a soportar lecturas parciales, que es justo la
        // clase de detalle que un S3 y un disco resuelven distinto.
        return File(receipt.Stream, receipt.ContentType, receipt.FileName);
    }
}
