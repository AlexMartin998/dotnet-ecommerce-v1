using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Service;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Http;
using ApiEcommerce.Shared.Paging;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Controllers;


[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")] // api/v1/category
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
[Authorize]
public class CategoryController : ControllerBase
{
    private readonly ICategoryService _service;

    public CategoryController(ICategoryService service)
    {
        _service = service;
    }

    // Sin try/catch: las excepciones de dominio que lance el servicio las traduce
    // GlobalExceptionHandler a ProblemDetails (ver AGENTS/docs/04-error-handling.md).

    [AllowAnonymous]
    [HttpGet(Name = "GetCategories")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public async Task<ActionResult<IEnumerable<CategoryDto>>> GetCategories(CancellationToken ct)
    {
        var result = await _service.GetAllAsync(ct);
        return Ok(result);
    }

    /// <summary>Listado paginado. Preferir este a <c>GET /api/v1/category</c>, que trae la tabla entera.</summary>
    [AllowAnonymous]
    [HttpGet("paged", Name = "GetCategoriesPaged")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<PagedResult<CategoryDto>>> GetCategoriesPaged(
        [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // Una página fuera de rango devuelve 200 con [] y el total real, no 404.
        var result = await _service.GetPagedAsync(query, ct);
        return Ok(result);
    }

    [AllowAnonymous]
    [HttpGet("{id:int}", Name = "GetCategory")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<CategoryDto>> GetCategory(int id, CancellationToken ct)
    {
        // GetByIdAsync lanza NotFoundAppException si no existe -> 404
        var category = await _service.GetByIdAsync(id, ct);
        return Ok(category);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPost(Name = "CreateCategory")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> CreateCategory([FromBody] CreateCategoryDto dto, CancellationToken ct)
    {
        // es obligatorio esto?  ASP.NET ya valida automaticamente esto
        // if (!ModelState.IsValid)
        //     return ValidationProblem(ModelState);

        // nombre duplicado -> ConflictAppException -> 409
        var newId = await _service.CreateAsync(dto, ct);
        return CreatedAtRoute("GetCategory", new { version = HttpContext.ApiVersionValue(), id = newId }, null);
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpPatch("{id:int}", Name = "UpdateCategory")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> UpdateCategory(int id, [FromBody] UpdateCategoryDto dto, CancellationToken ct)
    {
        // if (!ModelState.IsValid)
        //     return ValidationProblem(ModelState);

        await _service.UpdateAsync(id, dto, ct);
        return NoContent();
    }

    [Authorize(Roles = Roles.Admin)]
    [HttpDelete("{id:int}", Name = "DeleteCategory")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> DeleteCategory(int id, CancellationToken ct)
    {
        // con productos asociados -> ConflictAppException -> 409
        await _service.DeleteAsync(id, ct);
        return NoContent();
    }
}




/* using ApiEcommerce.Models;
using ApiEcommerce.Models.Dtos;
using ApiEcommerce.Repository;
using AutoMapper;
using Microsoft.AspNetCore.Mvc;


namespace ApiEcommerce.Controllers
{
    [Route("api/[controller]")] // api/category <- category xq es el nombre del controlador `Category`Controller
    [ApiController]
    public class CategoryController : ControllerBase
    {

        // DI -----
        private readonly ICategoryRepository _categoryRepository;
        private readonly IMapper _mapper;

        public CategoryController(ICategoryRepository categoryRepository, IMapper mapper)
        {
            _categoryRepository = categoryRepository;
            _mapper = mapper;
        }



        // endpoints -----
    [HttpGet(Name = "GetCategories")] // api/category
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public ActionResult<IEnumerable<CategoryDto>> GetCategories()
        {
            var categories = _categoryRepository.GetCategories();
            var categoriesDto = new List<CategoryDto>();
            foreach (var category in categories)
            {
                categoriesDto.Add(_mapper.Map<CategoryDto>(category));
            }
            return Ok(categoriesDto);
        }

    [HttpGet("{id:int}", Name = "GetCategory")] // api/category/1
        [ProducesResponseType(StatusCodes.Status200OK)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult GetCategory(int id)
        {
            var category = _categoryRepository.GetCategory(id);
            if (category == null)
            {
                return NotFound($"Category with id {id} not found");
            }
            var categoryDto = _mapper.Map<CategoryDto>(category);
            return Ok(categoryDto);
        }


        [HttpPost(Name = "CreateCategory")] // api/category
        [ProducesResponseType(StatusCodes.Status201Created)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        public IActionResult CreateCategory([FromBody] CreateCategoryDto createCategoryDto)
        {
            if (createCategoryDto == null)
            {
                return BadRequest(ModelState);
            }

            if (_categoryRepository.CategoryExists(createCategoryDto.Name))
            {
                ModelState.AddModelError("CustomError", "Category already exists!");
                return StatusCode(404, ModelState);
            }

            var category = _mapper.Map<Category>(createCategoryDto);

            if (!_categoryRepository.CreateCategory(category))
            {
                ModelState.AddModelError("CustomError", $"Something went wrong when saving the record {category.Name}");
                return StatusCode(500, ModelState);
            }

            return CreatedAtRoute("GetCategory", new { id = category.Id }, category);
        }


        [HttpPatch("{id:int}", Name = "UpdateCategory")] // api/category/1
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult UpdateCategory(int id, [FromBody] CreateCategoryDto updateCategoryDto)
        {
            if (updateCategoryDto == null || id <= 0)
            {
                return BadRequest(ModelState);
            }

            if (!_categoryRepository.CategoryExists(id))
            {
                return NotFound($"Category with id {id} not found");
            }

            var category = _mapper.Map<Category>(updateCategoryDto);
            category.Id = id;

            if (!_categoryRepository.UpdateCategory(category))
            {
                ModelState.AddModelError("CustomError", $"Something went wrong when updating the record {category.Name}");
                return StatusCode(500, ModelState);
            }

            return NoContent(); // 204
        }


        [HttpDelete("{id:int}", Name = "DeleteCategory")] // api/category/1
        [ProducesResponseType(StatusCodes.Status204NoContent)]
        [ProducesResponseType(StatusCodes.Status400BadRequest)]
        [ProducesResponseType(StatusCodes.Status401Unauthorized)]
        [ProducesResponseType(StatusCodes.Status404NotFound)]
        public IActionResult DeleteCategory(int id)
        {
            if (id <= 0)
            {
                return BadRequest(ModelState);
            }

            var category = _categoryRepository.GetCategory(id);
            if (category == null)
            {
                return NotFound($"Category with id {id} not found");
            }

            if (!_categoryRepository.DeleteCategory(category))
            {
                ModelState.AddModelError("CustomError", $"Something went wrong when deleting the record {category.Name}");
                return StatusCode(500, ModelState);
            }

            return NoContent(); // 204
        }

    }
}
 */