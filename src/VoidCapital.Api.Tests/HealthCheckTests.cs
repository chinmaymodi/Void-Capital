using System.Net;
using System.Net.Http.Json;
using VoidCapital.Api.Shared;
using VoidCapital.Api.Tests.Integration;

namespace VoidCapital.Api.Tests;

/// <summary>
/// Health endpoints against the container-backed host. The app's /api/health
/// probes PostgreSQL and Redis via ASP.NET health checks, so it can only
/// assert an exact 200 when those dependencies are actually healthy --
/// Testcontainers provides them deterministically for every run.
/// </summary>
[Collection("integration")]
public class HealthCheckTests
{
    private readonly IntegrationFactory _factory;

    public HealthCheckTests(IntegrationFactory factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk_WithDependenciesHealthy()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        // Both registered health checks (NpgSql + Redis) probe the live
        // containers, so the only stable outcome is 200 OK.
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
    }

    [Fact]
    public async Task HealthController_ReturnsStatusOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/health");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var body = await response.Content.ReadFromJsonAsync<HealthResponse>();
        Assert.NotNull(body);
        Assert.Equal("OK", body!.Status);
    }

    [Fact]
    public async Task SystemInfo_ReturnsProjectMetadata()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/system/info");

        // Assert
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);
        var envelope = await response.Content.ReadFromJsonAsync<ApiResponse<SystemInfoResponse>>();
        Assert.NotNull(envelope);
        Assert.True(envelope!.Success);
        Assert.Equal("Void Capital API", envelope.Data!.Name);
        Assert.Equal("0.1.0", envelope.Data.Version);
        Assert.False(string.IsNullOrWhiteSpace(envelope.Data.Environment));
    }

    [Fact]
    public async Task UnknownRoute_Returns404()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/v1/does-not-exist");

        // Assert
        Assert.Equal(HttpStatusCode.NotFound, response.StatusCode);
    }
}

public sealed record HealthResponse(string Status);

public sealed record SystemInfoResponse(string Name, string Version, string Environment);
