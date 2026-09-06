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
    private readonly IRefreshTokenService _sessions;
    private readonly RefreshTokenCookie _cookie;

    public AuthController(
        IAuthService service, IRefreshTokenService sessions, RefreshTokenCookie cookie)
    {
        _service = service;
        _sessions = sessions;
        _cookie = cookie;
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

        // Registrarse deja la sesión abierta, igual que el login: si no, el cliente
        // recibiría un access token que no puede renovar y en 15 minutos estaría fuera.
        await OpenSessionAsync(result.User.Id, ct);

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

        await OpenSessionAsync(result.User.Id, ct);

        return Ok(result);
    }

    /// <summary>Cambia un refresh token por un access token nuevo (y otro refresh token).</summary>
    /// <remarks>
    /// El refresh token NO viaja en el cuerpo: se lee de la cookie <c>HttpOnly</c>, que es
    /// justo lo que impide que un XSS se lo lleve. Por eso este endpoint no recibe DTO.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("refresh", Name = "RefreshToken")]
    [ProducesResponseType(StatusCodes.Status200OK)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponseDto>> Refresh(CancellationToken ct)
    {
        var current = _cookie.Read(Request);

        if (string.IsNullOrEmpty(current))
            return Unauthorized(Problem(
                "No refresh token was provided.", "missing_refresh_token"));

        // No existe, gastado o caducado -> 401. Y si es un reuso fuera de la ventana de
        // gracia, el servicio ya habrá revocado la familia entera antes de lanzar.
        var (auth, refreshed) = await _sessions.RotateAsync(current, ClientIp(), ct);

        _cookie.Write(Response, refreshed);

        return Ok(auth);
    }

    /// <summary>Cierra la sesión: revoca la familia de refresh tokens y borra la cookie.</summary>
    /// <remarks>
    /// <c>[AllowAnonymous]</c> a propósito: el caso más habitual de cerrar sesión es que
    /// el access token ya haya expirado. Exigir uno válido dejaría al usuario sin poder
    /// cerrar justo cuando más falta le hace.
    /// </remarks>
    [AllowAnonymous]
    [HttpPost("logout", Name = "Logout")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public async Task<IActionResult> Logout(CancellationToken ct)
    {
        // Si venía un access token válido, su `jti` entra en la denylist para que muera
        // ya en vez de al expirar. Es un extra: la sesión se corta con o sin él.
        await _sessions.LogoutAsync(
            _cookie.Read(Request), User.GetTokenId(), User.GetTokenExpiry(), ct);

        _cookie.Clear(Response);

        // 204 siempre, incluso sin cookie o con una ya revocada: cerrar sesión dos veces
        // tiene que ser inofensivo, y un error dejaría al usuario sin saber si salió.
        return NoContent();
    }

    /// <summary>Cambia la contraseña del propio usuario y corta el resto de sesiones.</summary>
    /// <remarks>
    /// ⚠️ Cambiar la contraseña <b>revoca todas las sesiones</b>, y esa es la expectativa de
    /// cualquiera que la cambia porque sospecha que se la han robado: si las sesiones
    /// abiertas siguieran vivas, cambiarla no habría echado a nadie.
    /// Se abre una sesión nueva para ESTE dispositivo, para no expulsar también a quien
    /// acaba de hacerlo bien.
    /// </remarks>
    [Authorize]
    [HttpPost("password", Name = "ChangePassword")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status400BadRequest)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType(StatusCodes.Status422UnprocessableEntity)]
    public async Task<IActionResult> ChangePassword(
        [FromBody] ChangePasswordDto dto, CancellationToken ct)
    {
        if (!ModelState.IsValid)
            return ValidationProblem(ModelState);

        var userId = User.GetRequiredUserId();

        // contraseña actual incorrecta -> 401 ; la nueva no cumple la política -> 422
        await _service.ChangePasswordAsync(userId, dto, ct);

        await _sessions.RevokeAllSessionsAsync(userId, ct);
        await OpenSessionAsync(userId, ct);

        return NoContent();
    }

    /// <summary>Cierra la sesión en TODOS los dispositivos.</summary>
    [Authorize]
    [HttpPost("logout-all", Name = "LogoutEverywhere")]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType(StatusCodes.Status401Unauthorized)]
    public async Task<IActionResult> LogoutEverywhere(CancellationToken ct)
    {
        var userId = User.GetRequiredUserId();

        await _sessions.RevokeAllSessionsAsync(userId, ct);

        // El access token de ESTE dispositivo sí se puede matar en el acto; los de los
        // demás no —la denylist va por `jti` y no sabemos cuáles son— y sobreviven como
        // mucho lo que les quede, sin poder renovarse.
        await _sessions.LogoutAsync(null, User.GetTokenId(), User.GetTokenExpiry(), ct);

        _cookie.Clear(Response);

        return NoContent();
    }

    // ---- helpers -----------------------------------------------------------

    /// <summary>Emite el refresh token de una sesión nueva y lo deja en la cookie.</summary>
    private async Task OpenSessionAsync(string userId, CancellationToken ct)
        => _cookie.Write(Response, await _sessions.IssueAsync(userId, ClientIp(), ct));

    /// <summary>Solo para investigar incidentes; no se usa para decidir nada.</summary>
    private string? ClientIp() => HttpContext.Connection.RemoteIpAddress?.ToString();

    private static ProblemDetails Problem(string detail, string code) => new()
    {
        Status = StatusCodes.Status401Unauthorized,
        Title = "Unauthorized",
        Detail = detail,
        Extensions = { ["code"] = code }
    };

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
