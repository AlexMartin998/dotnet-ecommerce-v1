using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Shared.Http.Health;


/// <summary>
/// Sonda de liveness: «¿el proceso está vivo y respondiendo?». No toca la base ni Redis;
/// la de readiness es <c>/health/ready</c>.
/// </summary>
/// <remarks>
/// Si la sonda de vida dependiera de la base, una caída de la base haría que el orquestador
/// reiniciara procesos sanos. <c>[ApiVersionNeutral]</c> es obligatorio: sin él el
/// versionador exige una versión que esta ruta no tiene y <c>/health</c> devuelve 404.
/// </remarks>
[ApiController]
[ApiVersionNeutral]
[AllowAnonymous]
[Route("health")]
public class HealthController : ControllerBase
{
    /// <summary>Responde 200 mientras el proceso atienda peticiones.</summary>
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
        => Ok(new { status = "UP" });
}
