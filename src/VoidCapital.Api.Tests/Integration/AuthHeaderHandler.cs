using System.Net.Http.Headers;
using System.Text.Json;
using VoidCapital.Api.Modules.Auth.Services;

namespace VoidCapital.Api.Tests.Integration;

/// <summary>
/// Injects the admin API key (from auth.keys.json, same file the API loads)
/// into every request so integration tests exercise the authenticated paths.
/// </summary>
public class AuthHeaderHandler : DelegatingHandler
{
    private readonly string _adminKey;

    public AuthHeaderHandler()
    {
        var path = Path.Combine(AppContext.BaseDirectory, "auth.keys.json");
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        _adminKey = doc.RootElement.GetProperty("admin").GetString()
            ?? throw new InvalidOperationException("auth.keys.json is missing the admin key.");
    }

    protected override Task<HttpResponseMessage> SendAsync(
        HttpRequestMessage request, CancellationToken cancellationToken)
    {
        request.Headers.Authorization = null;
        request.Headers.TryAddWithoutValidation(
            ApiKeyAuthenticationHandler.HeaderName, _adminKey);
        return base.SendAsync(request, cancellationToken);
    }
}