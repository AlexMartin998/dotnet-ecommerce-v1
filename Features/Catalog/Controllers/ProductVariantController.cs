using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Http;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Net.Http.Headers;
using ApiEcommerce.Features.Catalog.Dtos;
using ApiEcommerce.Features.Catalog.Service;

namespace ApiEcommerce.Features.Catalog.Controllers;


/// <summary>Tallas de un producto: listado, alta y edición. Solo administración.</summary>
/// <remarks>
/// La ficha pública ya trae las variantes activas dentro de <c>ProductDto</c>; esto es el
/// panel, que necesita también las desactivadas. No hay DELETE: una talla se desactiva.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/product/{productId:int}/variants")]
[Produces("application/json")]
[Authorize(Roles = Roles.Admin)]
public class ProductVariantController : ControllerBase
{
    private readonly IProductVariantService _service;

    public ProductVariantController(IProductVariantService service)
    {
        _service = service;
    }

    [HttpGet(Name = "GetProductVariants")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<IReadOnlyList<ProductVariantDto>>> GetProductVariants(
        int productId, CancellationToken ct)
        => Ok(await _service.GetForProductAsync(productId, ct));

    /// <summary>Añade una talla. El SKU, si no se manda, es <c>{SKU del producto}-{talla}</c>.</summary>
    [HttpPost(Name = "AddProductVariant")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductVariantDto>> AddProductVariant(
        int productId, [FromBody] CreateProductVariantDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var variant = await _service.AddAsync(productId, dto, ct);

        // Sin ruta de detalle propia: el Location apunta al listado del producto, que la trae.
        return CreatedAtRoute("GetProductVariants",
            new { version = HttpContext.ApiVersionValue(), productId }, variant);
    }

    /// <summary>Repone stock, reordena o (des)activa. Admite <c>If-Match</c> con el <c>rowVersion</c>.</summary>
    [HttpPatch("{variantId:int}", Name = "UpdateProductVariant")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status412PreconditionFailed)]
    public async Task<ActionResult<ProductVariantDto>> UpdateProductVariant(
        int productId, int variantId, [FromBody] UpdateProductVariantDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        dto.IfMatch = IfMatchTags();

        var variant = await _service.UpdateAsync(productId, variantId, dto, ct);

        if (variant.RowVersion is not null)
            Response.Headers.ETag = $"\"{variant.RowVersion}\"";

        return Ok(variant);
    }

    /// <summary>Los tokens fuertes de <c>If-Match</c>, sin comillas. Igual que en <c>ProductController</c>.</summary>
    private IReadOnlyList<string>? IfMatchTags()
    {
        if (!EntityTagHeaderValue.TryParseList(Request.Headers.IfMatch, out var tags) || tags is null)
            return null;

        var strong = tags
            .Where(tag => !tag.IsWeak && tag.Tag.HasValue && tag.Tag != "*")
            .Select(tag => tag.Tag.Value!.Trim('"'))
            .ToList();

        return strong.Count > 0 ? strong : null;
    }
}
