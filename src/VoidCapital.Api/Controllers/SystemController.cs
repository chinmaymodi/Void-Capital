using Microsoft.AspNetCore.Mvc;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Controllers;

[ApiController]
[Route("api/v1/system")]
public class SystemController : ControllerBase
{
    private readonly IWebHostEnvironment _env;

    public SystemController(IWebHostEnvironment env)
    {
        _env = env;
    }

    [HttpGet("info")]
    public ActionResult<ApiResponse<object>> GetInfo()
    {
        return Ok(ApiResponse<object>.Ok(new
        {
            name = "Void Capital API",
            version = "0.1.0",
            environment = _env.EnvironmentName
        }));
    }
}
