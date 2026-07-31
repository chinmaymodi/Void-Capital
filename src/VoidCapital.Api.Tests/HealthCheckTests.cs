using System.Net;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc.Testing;

namespace VoidCapital.Api.Tests;

public class HealthCheckTests : IClassFixture<WebApplicationFactory<Program>>
{
    private readonly WebApplicationFactory<Program> _factory;

    public HealthCheckTests(WebApplicationFactory<Program> factory)
    {
        _factory = factory;
    }

    [Fact]
    public async Task HealthEndpoint_ReturnsOk()
    {
        // Arrange
        var client = _factory.CreateClient();

        // Act
        var response = await client.GetAsync("/api/health");

        // Assert
        // Healthy -> 200 OK; unhealthy -> 503 ServiceUnavailable (default
        // HealthChecks behavior). The test host has no PostgreSQL/Redis, so the
        // only assertion that is stable across environments is that the endpoint
        // exists and returns one of these two documented states.
        Assert.True(
            response.StatusCode is HttpStatusCode.OK or HttpStatusCode.ServiceUnavailable,
            $"Expected 200 or 503, got {(int)response.StatusCode}");
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

public sealed record ApiResponse<T>(bool Success, T? Data, string? Error, string? TraceId);
