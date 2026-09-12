using ApiEcommerce.Features.Ordering.Dtos;
using ApiEcommerce.Features.Ordering.Service;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Features.Ordering.Controllers;


[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")]   // api/v1/cart
[Produces("application/json")]
// Anónimo: el carrito existe antes que la sesión, y pedir la cuenta para ver el precio
// de lo que ya se está mirando es poner la cuenta por delante del producto.
[AllowAnonymous]
public class CartController : ControllerBase
{
    private readonly ICartService _service;

    public CartController(ICartService service)
    {
        _service = service;
    }

    /// <summary>Cotiza un carrito con los precios y el stock de este instante.</summary>
    /// <remarks>
    /// No aparta stock ni compromete el precio: lo que se cobra lo decide
    /// <c>POST /order</c>. Una línea sin stock vuelve marcada, no rompe la respuesta.
    /// </remarks>
    [HttpPost("quote", Name = "QuoteCart")]
    [RequestSizeLimit(64 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<CartQuoteDto>> Quote(
        [FromBody] QuoteCartDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        return Ok(await _service.QuoteAsync(dto, ct));
    }
}
