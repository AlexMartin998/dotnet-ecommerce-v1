using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Idempotency;
using ApiEcommerce.Shared.Paging;
using ApiEcommerce.Shared.Storage;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Features.Catalog.Controllers;


/// <summary>Endpoints de productos: CRUD, búsqueda, imagen y compra.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")] // api/v1/product
[Produces("application/json")]
// Cerrado por defecto. Varios [Authorize] se combinan (AND) y solo [AllowAnonymous]
// gana sobre la clase, así que aquí va el requisito más débil (estar autenticado) y
// cada acción añade el suyo.
[Authorize]
public class ProductController : ControllerBase
{
    private readonly IProductService _service;

    public ProductController(IProductService service)
    {
        _service = service;
    }

    // ---- CRUD ------------------------------------------------------------

    [AllowAnonymous]
    [HttpGet(Name = "GetProducts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(CancellationToken ct)
    {
        var result = await _service.GetAllAsync(ct);
        return Ok(result);
    }

    /// <summary>Listado paginado. Preferir este a <c>GET /api/v1/product</c>, que trae la tabla entera.</summary>
    [AllowAnonymous]
    [HttpGet("paged", Name = "GetProductsPaged")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<ProductDto>>> GetProductsPaged(
        [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var result = await _service.GetPagedAsync(query, ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id:int}", Name = "GetProduct")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetProduct(int id, CancellationToken ct)
    {
        var product = await _service.GetByIdAsync(id, ct);

        // Entre comillas porque el RFC 9110 lo exige para un ETag.
        if (product.RowVersion is not null)
            Response.Headers.ETag = $"\"{product.RowVersion}\"";

        return Ok(product);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost(Name = "CreateProduct")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateProduct([FromBody] CreateProductDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // CategoryId inexistente -> 400 ; SKU duplicado -> 409
        var newId = await _service.CreateAsync(dto, ct);
        return CreatedAtRoute("GetProduct", new { version = HttpContext.ApiVersionValue(), id = newId }, null);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPatch("{id:int}", Name = "UpdateProduct")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] UpdateProductDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // Se rellena aquí para que el servicio no conozca HttpContext. Es opcional:
        // sin If-Match el PATCH se comporta como siempre.
        dto.IfMatch = IfMatchTags();

        await _service.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:int}", Name = "DeleteProduct")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }

    // ---- endpoints propios del dominio -----------------------------------

    [AllowAnonymous]
    [HttpGet("category/{categoryId:int}", Name = "GetProductsForCategory")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProductsForCategory(
        int categoryId, CancellationToken ct)
    {
        var result = await _service.GetForCategoryAsync(categoryId, ct);
        return Ok(result);
    }

    /// <summary>Búsqueda por nombre. Sin resultados devuelve 200 con lista vacía, no 404.</summary>
    [AllowAnonymous]
    [HttpGet("search", Name = "SearchProducts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> SearchProducts(
        [FromQuery] string? name, CancellationToken ct)
    {
        var result = await _service.SearchAsync(name ?? string.Empty, ct);
        return Ok(result);
    }

    /// <summary>Sube o reemplaza la imagen del producto.</summary>
    /// <remarks>
    /// <c>[Consumes]</c> hace que un cliente que mande JSON reciba un 415 claro, y
    /// <c>[RequestSizeLimit]</c> corta la petición antes de leerla entera.
    /// </remarks>
    [Authorize(Roles = Roles.Admin)]
    [HttpPost("{id:int}/image", Name = "SetProductImage")]
    [Consumes("multipart/form-data")]
    [RequestSizeLimit(2 * 1024 * 1024)]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> SetProductImage(
        int id, IFormFile file, CancellationToken ct)
    {
        if (file is null || file.Length == 0)
            return ValidationProblem("A file is required.");

        // IFormFile se adapta a FileUpload: el servicio no debe conocer ASP.NET Core.
        await using var content = file.OpenReadStream();

        var result = await _service.SetImageAsync(
            id, new FileUpload(content, file.FileName, file.ContentType, file.Length), ct);

        return Ok(result);
    }

    /// <summary>Descuenta stock por SKU. Hereda el [Authorize] de la clase: cualquier usuario autenticado.</summary>
    [HttpPost("buy", Name = "BuyProduct")]
    // Atajo, no la garantía: corta el reintento sin tocar la base. Quien garantiza que no
    // se compra dos veces es ProductService, que abre además su propia transacción.
    [Idempotent]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<ProductDto>> BuyProduct([FromBody] BuyProductDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // La cabecera de idempotencia se traduce aquí a una intención de dominio.
        var intent = HttpContext.CommandIntentFor("catalog.buy-product");

        // SKU inexistente -> 404 ; stock insuficiente -> 409 ; clave reusada -> 422
        var outcome = await _service.BuyAsync(dto, intent, User.GetUserId(), ct);

        // Marca todos los replays, no solo los que resuelve el atajo de Redis.
        if (outcome.WasReplayed)
            Response.Headers[IdempotentAttribute.ReplayedHeader] = "true";

        return Ok(outcome.Result);
    }

    /// <summary>
    /// Lee <c>If-Match</c> y devuelve los tokens fuertes ya sin comillas.
    /// </summary>
    /// <remarks>
    /// Se parsea con el tipo del framework porque la cabecera admite una lista y la
    /// precondición se cumple si alguna etiqueta casa.
    /// </remarks>
    private IReadOnlyList<string>? IfMatchTags()
    {
        if (!EntityTagHeaderValue.TryParseList(Request.Headers.IfMatch, out var tags) || tags is null)
            return null;

        var strong = tags
            // RFC 9110 §13.1.1: If-Match exige comparación fuerte, un W/"..." no vale.
            .Where(tag => !tag.IsWeak && tag.Tag.HasValue && tag.Tag != "*")
            .Select(tag => tag.Tag.Value!.Trim('"'))
            .ToList();

        return strong.Count > 0 ? strong : null;
    }
}
