using System.Reflection;
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
        // S6: version comes from assembly metadata (InformationalVersion
        // carries the SDK-style version, plus the source revision when
        // SourceLink is enabled), never a hardcoded string.
        var version = typeof(SystemController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion ?? "0.0.0";

        return Ok(ApiResponse<object>.Ok(new
        {
            name = "Void Capital API",
            version,
            environment = _env.EnvironmentName
        }));
    }
}
