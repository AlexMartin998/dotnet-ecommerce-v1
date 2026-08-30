using ApiEcommerce.Shared.Auth;
using ApiEcommerce.Shared.Http;
using Microsoft.AspNetCore.RateLimiting;
using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using ApiEcommerce.Features.Accounts.Dtos;
using ApiEcommerce.Features.Accounts.Service;

namespace ApiEcommerce.Features.Accounts.Controllers;


/// <summary>Registro, login y perfil del usuario autenticado.</summary>
[ApiController]
[ApiVersion("1.0")]
[Route("api/v{version:apiVersion}/[controller]")] // api/v1/auth
[Produces("application/json")]
// Ventana estricta: 10 intentos por minuto y por IP. Complementa al lockout de
// Identity, que solo cuenta fallos POR USUARIO.
[EnableRateLimiting(RateLimitPolicies.Auth)]
public class AuthController : ControllerBase
{
    private readonly IAuthService _service;

    public AuthController(IAuthService service)
    {
        _service = service;
    }

    /// <summary>Alta de un usuario nuevo. Siempre con rol <c>user</c>.</summary>
    /// <remarks>
    /// El rol <b>no</b> se acepta como campo del body a propósito: en el código de
    /// referencia el cliente podía mandar <c>"Role": "Admin"</c> en un endpoint
    /// anónimo y auto-promoverse. Los administradores se crean sembrando o
    /// promoviendo desde un endpoint protegido, nunca desde el registro público.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("register", Name = "Register")]
    [ProducesResponseType(StatusCodes.Status201Created)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status409Conflict)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<ActionResult<AuthResponseDto>> Register(
        [FromBody] RegisterUserDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // username/email repetido -> 409 ; política de password -> 422
        var result = await _service.RegisterAsync(dto, ct);

        return CreatedAtRoute("GetProfile", new { version = "1.0" }, result);
    }

    /// <summary>Valida credenciales y devuelve el access token.</summary>
    [AllowAnonymous]
    [HttpPost("login", Name = "Login")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<AuthResponseDto>> Login(
        [FromBody] LoginUserDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        // credenciales inválidas -> 401 ; cuenta bloqueada -> 403
        var result = await _service.LoginAsync(dto, ct);
        return Ok(result);
    }

    /// <summary>Perfil del portador del token.</summary>
    [Authorize]
    [HttpGet("me", Name = "GetProfile")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetProfile(CancellationToken ct)
    {
        var profile = await _service.GetProfileAsync(User.GetRequiredUserId(), ct);
        return Ok(profile);
    }
}
