using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Features.Accounts.Service;
using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Paging;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Features.Accounts.Controllers;


/// <summary>
/// Administración de cuentas. <b>Todo el controller es solo para administradores.</b>
/// </summary>
/// <remarks>
/// Existe porque hasta ahora el <b>único</b> camino para tener un administrador era el
/// <c>DataSeeder</c>: no había forma de promover a nadie, ni de bloquear una cuenta, sin
/// tocar la base a mano.
/// </remarks>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")] // api/v1/user
[Produces("application/json")]
// El requisito va en la CLASE porque aquí sí es el mismo para todas las acciones. Ojo con
// la semántica: varios [Authorize] se COMBINAN (AND), así que una acción no podría
// relajarlo — solo [AllowAnonymous] gana, y aquí no lo lleva ninguna.
[Authorize(Roles = Roles.Admin)]
public class UserController : ControllerBase
{
    private readonly IUserAdminService _service;

    public UserController(IUserAdminService service)
    {
        _service = service;
    }

    /// <summary>Listado paginado de usuarios.</summary>
    [HttpGet(Name = "GetUsers")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<PagedResult<UserDto>>> GetUsers(
        [FromQuery] PageQuery query, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        return Ok(await _service.GetPagedAsync(query, ct));
    }

    /// <summary>Detalle de un usuario.</summary>
    [HttpGet("{id}", Name = "GetUser")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetUser(string id, CancellationToken ct)
        => Ok(await _service.GetByIdAsync(id, ct));

    /// <summary>Da un rol a un usuario.</summary>
    /// <remarks>Idempotente: asignar un rol que ya se tiene devuelve 204 y no cambia nada.</remarks>
    [HttpPost("{id}/roles", Name = "AssignRole")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> AssignRole(
        string id, [FromBody] AssignRoleDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // rol inexistente -> 400 ; usuario inexistente -> 404
        await _service.AssignRoleAsync(id, dto.Role, User.GetRequiredUserId(), ct);

        return NoContent();
    }

    /// <summary>Quita un rol a un usuario.</summary>
    [HttpDelete("{id}/roles/{role}", Name = "RemoveRole")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> RemoveRole(string id, string role, CancellationToken ct)
    {
        // quitarse el admin a uno mismo, o dejar el sistema sin administradores -> 409
        await _service.RemoveRoleAsync(id, role, User.GetRequiredUserId(), ct);

        return NoContent();
    }

    /// <summary>Bloquea una cuenta y corta sus sesiones abiertas.</summary>
    [HttpPost("{id}/lock", Name = "LockUser")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    public async Task<IActionResult> Lock(string id, CancellationToken ct)
    {
        // bloquearse a uno mismo -> 409
        await _service.LockAsync(id, User.GetRequiredUserId(), ct);

        return NoContent();
    }

    /// <summary>Levanta el bloqueo de una cuenta.</summary>
    [HttpPost("{id}/unlock", Name = "UnlockUser")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> Unlock(string id, CancellationToken ct)
    {
        await _service.UnlockAsync(id, User.GetRequiredUserId(), ct);

        return NoContent();
    }
}
