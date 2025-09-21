using Microsoft.AspNetCore.Mvc;

namespace ApiEcommerce.Controllers;

[ApiController]
[Route("health")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
        => Ok(new { status = "UP" });
}
