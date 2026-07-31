using Microsoft.AspNetCore.Mvc;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class HealthController : ControllerBase
{
    [HttpGet]
    public IActionResult Get()
    {
        return Ok(new { Status = "OK" });
    }
}
