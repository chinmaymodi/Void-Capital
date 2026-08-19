using System.Reflection;
using FluentAssertions;
using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc;
using Moq;
using VoidCapital.Api.Controllers;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Controllers;

/// <summary>
/// S6: the reported version must come from assembly metadata, never a
/// hardcoded string that drifts from the actual build.
/// </summary>
public class SystemControllerTests
{
    [Fact]
    public void GetInfo_ReportsVersionFromAssemblyMetadata()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Development");
        var controller = new SystemController(env.Object);

        var result = controller.GetInfo();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        envelope.Success.Should().BeTrue();

        var expectedVersion = typeof(SystemController).Assembly
            .GetCustomAttribute<AssemblyInformationalVersionAttribute>()?
            .InformationalVersion;
        expectedVersion.Should().NotBeNullOrWhiteSpace();

        // The anonymous response type is internal to the API assembly, so
        // dynamic binding cannot reach it from here; read via reflection.
        var version = envelope.Data!.GetType().GetProperty("version")!.GetValue(envelope.Data) as string;
        version.Should().Be(expectedVersion);
        version.Should().NotBe("0.1.0"); // the old hardcoded value
    }

    [Fact]
    public void GetInfo_ReportsEnvironmentName()
    {
        var env = new Mock<IWebHostEnvironment>();
        env.Setup(e => e.EnvironmentName).Returns("Production");
        var controller = new SystemController(env.Object);

        var result = controller.GetInfo();

        var ok = result.Result.Should().BeOfType<OkObjectResult>().Subject;
        var envelope = ok.Value.Should().BeOfType<ApiResponse<object>>().Subject;
        var environment = envelope.Data!.GetType().GetProperty("environment")!.GetValue(envelope.Data) as string;
        environment.Should().Be("Production");
    }
}