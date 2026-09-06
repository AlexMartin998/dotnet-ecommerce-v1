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


[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")] // api/v1/product
[Produces("application/json")]
// Cerrado por defecto: sin ningún atributo, una acción de este controller exige
// estar autenticado. Los GET públicos se abren con [AllowAnonymous] y las
// escrituras se restringen con [Authorize(Roles = ...)] una a una.
//
// OJO con la semántica de ASP.NET Core: varios [Authorize] se COMBINAN (AND), no se
// sobreescriben. Poner [Authorize(Roles = "admin")] en la clase y [Authorize] en una
// acción NO relaja nada: la acción seguiría exigiendo el rol admin. El único atributo
// que gana sobre la clase es [AllowAnonymous]. Por eso la clase lleva el requisito
// más DÉBIL (estar autenticado) y cada acción añade el suyo.
//
// La compra es justo el caso que obliga a este diseño: solo pide estar autenticado,
// con cualquier rol. Exigir admin para comprar —como hacía el código de referencia—
// no tiene sentido en una tienda.
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

        // ETag: la versión del recurso, para que el cliente pueda devolverla en If-Match
        // y no pisar el cambio de otro. Entre comillas porque el RFC 9110 lo exige.
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
    // La respuesta central de If-Match, y el 400 del token ilegible. Sin declararlas,
    // Swagger no documenta lo único que un cliente necesita saber para usar la feature.
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] UpdateProductDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // La versión viaja por cabecera, no en el cuerpo: es una precondición de HTTP.
        // Se rellena aquí para que el servicio y las reglas no tengan que conocer
        // HttpContext. Es OPCIONAL: sin If-Match, el PATCH se comporta como siempre.
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

    // Sin resultados devuelve 200 con [], no 404 (regla 7 de 04-error-handling.md)
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
    /// <c>[Consumes]</c> es explícito para que Swagger pinte el selector de archivo y
    /// para que un cliente que mande JSON reciba un 415 claro en vez de un 400 raro.
    /// El <c>[RequestSizeLimit]</c> corta la petición <b>antes</b> de leerla entera:
    /// validar el tamaño solo después de haber recibido 500 MB no protege de nada.
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

        // El controller adapta el tipo del framework (IFormFile) al del dominio
        // (FileUpload): el servicio no debe conocer ASP.NET Core.
        await using var content = file.OpenReadStream();

        var result = await _service.SetImageAsync(
            id, new FileUpload(content, file.FileName, file.ContentType, file.Length), ct);

        return Ok(result);
    }

    /// <summary>Descuenta stock por SKU.</summary>
    // Sin atributo: hereda el [Authorize] de la clase = cualquier usuario autenticado.
    [HttpPost("buy", Name = "BuyProduct")]
    [Idempotent]   // reintentar con la misma Idempotency-Key no vuelve a descontar stock
    // Sin [Transactional]: la transacción la abre ProductService con ITransactionRunner,
    // que sí es compatible con la estrategia de reintentos de EF.
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> BuyProduct([FromBody] BuyProductDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // SKU inexistente -> 404 ; stock insuficiente -> 409
        var product = await _service.BuyAsync(dto, User.GetUserId(), ct);
        return Ok(product);
    }

    /// <summary>
    /// Lee <c>If-Match</c> y le quita el envoltorio del RFC (comillas y el prefijo
    /// <c>W/</c> de las etiquetas débiles).
    /// </summary>
    /// <remarks>
    /// Sin quitarlo, el token no casaría nunca con el de la base y todo PATCH con
    /// <c>If-Match</c> devolvería 412 — un fallo especialmente desagradable porque parece
    /// un conflicto real.
    /// </remarks>
    private IReadOnlyList<string>? IfMatchTags()
    {
        // ⚠️ Se parsea con el tipo del framework y no a mano. `If-Match` admite una LISTA
        // (`If-Match: "a", "b"`) y la precondición se cumple si ALGUNA casa; tratarlo como
        // un token único devolvía 400 a un cliente conforme —y a cualquiera que mandara la
        // cabecera dos veces, porque StringValues las une con coma—.
        //
        // El `TrimStart('W', '/')` anterior además era una trampa latente: con un ETag sin
        // comillas que empezara por 'W' o '/' se comía caracteres del token. Hoy no podía
        // pasar (el base64 de un rowversion empieza siempre por 'A'), pero dependía de un
        // detalle del formato del dato, no del código.
        if (!EntityTagHeaderValue.TryParseList(Request.Headers.IfMatch, out var tags) || tags is null)
            return null;

        var strong = tags
            // El RFC 9110 §13.1.1 exige comparación FUERTE en If-Match: un validador débil
            // (`W/"..."`) no puede satisfacer la precondición. Antes se aceptaba.
            .Where(tag => !tag.IsWeak && tag.Tag.HasValue && tag.Tag != "*")
            .Select(tag => tag.Tag.Value!.Trim('"'))
            .ToList();

        return strong.Count > 0 ? strong : null;
    }
}
