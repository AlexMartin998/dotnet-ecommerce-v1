using Asp.Versioning;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Shared.Http.Health;


/// <summary>
/// Sonda de <b>liveness</b>: "¿el proceso está vivo y respondiendo?".
/// No toca la base ni Redis a propósito — si la sonda de vida dependiera de la base,
/// una caída de la base haría que el orquestador reiniciara procesos sanos.
/// La sonda de <b>readiness</b> (que sí comprueba dependencias) es <c>/health/ready</c>.
/// </summary>
/// <remarks>
/// <c>[ApiVersionNeutral]</c> es obligatorio desde que la API está versionada: sin él,
/// el versionador exige una versión que esta ruta no tiene y <c>/health</c> devuelve 404.
/// Y es lo correcto: una sonda de infraestructura no forma parte del contrato
/// versionado de la API.
/// </remarks>
[ApiController]
[ApiVersionNeutral]
[AllowAnonymous]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    [ProducesResponseType(StatusCodes.Status200OK)]
    public IActionResult Get()
        => Ok(new { status = "UP" });
}
