using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Service;
using ApiEcommerce.Shared.Db;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Controllers;


[ApiController]
[Route("api/[controller]")] // api/product
[Produces("application/json")]
public class ProductController : ControllerBase
{
    private readonly IProductService _service;

    public ProductController(IProductService service)
    {
        _service = service;
    }

    // ---- CRUD ------------------------------------------------------------

    [HttpGet(Name = "GetProducts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> GetProducts(CancellationToken ct)
    {
        var result = await _service.GetAllAsync(ct);
        return Ok(result);
    }

    [HttpGet("{id:int}", Name = "GetProduct")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<ProductDto>> GetProduct(int id, CancellationToken ct)
    {
        var product = await _service.GetByIdAsync(id, ct);
        return Ok(product);
    }

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
        return CreatedAtRoute("GetProduct", new { id = newId }, null);
    }

    [HttpPatch("{id:int}", Name = "UpdateProduct")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateProduct(int id, [FromBody] UpdateProductDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        await _service.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [HttpDelete("{id:int}", Name = "DeleteProduct")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> DeleteProduct(int id, CancellationToken ct)
    {
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }

    // ---- endpoints propios del dominio -----------------------------------

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
    [HttpGet("search", Name = "SearchProducts")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<ProductDto>>> SearchProducts(
        [FromQuery] string? name, CancellationToken ct)
    {
        var result = await _service.SearchAsync(name ?? string.Empty, ct);
        return Ok(result);
    }

    /// <summary>Descuenta stock por SKU.</summary>
    [HttpPost("buy", Name = "BuyProduct")]
    [Transactional] // la compra escribirá en más de un repositorio en cuanto haya Order
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<ProductDto>> BuyProduct([FromBody] BuyProductDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // SKU inexistente -> 404 ; stock insuficiente -> 409
        var product = await _service.BuyAsync(dto, ct);
        return Ok(product);
    }
}
