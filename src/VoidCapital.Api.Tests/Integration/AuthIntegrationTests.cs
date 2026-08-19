using System.Net;
using System.Net.Http.Headers;
using System.Text.Json;
using FluentAssertions;
using VoidCapital.Api.Modules.Auth.Services;
using Xunit;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// A1-class auth behavior over the real HTTP pipeline: missing key -> 401,
/// admin key -> full access, user key -> own data only (403 on other users
/// and on admin endpoints), health stays public.
/// </summary>
[Collection("integration")]
public class AuthIntegrationTests
{
    private readonly IntegrationFactory _factory;
    private readonly HttpClient _client;
    private readonly string _adminKey;
    private readonly Dictionary<string, string> _userKeys;

    public AuthIntegrationTests(IntegrationFactory factory)
    {
        _factory = factory;
        _client = factory.CreateAuthedClient();
        var path = Path.Combine(AppContext.BaseDirectory, "auth.keys.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        _adminKey = doc.RootElement.GetProperty("admin").GetString()!;
        _userKeys = doc.RootElement.GetProperty("users").EnumerateObject()
            .ToDictionary(p => p.Name, p => p.Value.GetString()!);
    }

    private HttpClient BareClient() => _factory.CreateClient();

    private static HttpRequestMessage Get(string url, string? key)
    {
        var request = new HttpRequestMessage(HttpMethod.Get, url);
        if (key is not null)
            request.Headers.TryAddWithoutValidation(ApiKeyAuthenticationHandler.HeaderName, key);
        return request;
    }

    [Fact]
    public async Task MissingKey_Returns401Envelope()
    {
        var response = await BareClient().SendAsync(Get("/api/v1/portfolio/1", null));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("\"success\":false");
        body.Should().Contain("Unauthorized");
    }

    [Fact]
    public async Task InvalidKey_Returns401()
    {
        var response = await BareClient().SendAsync(Get("/api/v1/portfolio/1", "not-a-real-key"));

        response.StatusCode.Should().Be(HttpStatusCode.Unauthorized);
    }

    [Fact]
    public async Task AdminKey_AccessesAnyUserPortfolio()
    {
        var response = await BareClient().SendAsync(Get("/api/v1/portfolio/1", _adminKey));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserKey_AccessesOwnPortfolio()
    {
        var userKey = _userKeys["1"];

        var response = await BareClient().SendAsync(Get("/api/v1/portfolio/1", userKey));

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }

    [Fact]
    public async Task UserKey_ForbiddenOnOtherUsersPortfolio()
    {
        var userKey = _userKeys["2"];

        var response = await BareClient().SendAsync(Get("/api/v1/portfolio/1", userKey));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
        var body = await response.Content.ReadAsStringAsync();
        body.Should().Contain("Forbidden");
    }

    [Fact]
    public async Task UserKey_ForbiddenOnAdminEndpoint()
    {
        var userKey = _userKeys["1"];

        var response = await BareClient().SendAsync(Get("/api/v1/admin/status", userKey));

        response.StatusCode.Should().Be(HttpStatusCode.Forbidden);
    }

    [Fact]
    public async Task Health_StaysPublic()
    {
        var response = await BareClient().GetAsync("/api/health");

        response.StatusCode.Should().Be(HttpStatusCode.OK);
    }
}