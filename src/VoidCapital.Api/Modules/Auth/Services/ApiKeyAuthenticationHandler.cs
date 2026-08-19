using System.Security.Claims;
using System.Text.Encodings.Web;
using Microsoft.AspNetCore.Authentication;
using Microsoft.Extensions.Options;
using VoidCapital.Api.Modules.Auth.Models;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Modules.Auth.Services;

/// <summary>
/// API-key authentication. The X-Api-Key header is matched against the keys
/// in auth.keys.json: the admin key yields the Admin role; a user key yields
/// the User role with a NameIdentifier claim of the user id. 401/403 responses
/// use the standard ApiResponse envelope so the frontend error interceptor
/// keeps working.
/// </summary>
public class ApiKeyAuthenticationHandler : AuthenticationHandler<AuthenticationSchemeOptions>
{
    public const string SchemeName = "ApiKey";
    public const string HeaderName = "X-Api-Key";

    private readonly AuthKeys _keys;

    public ApiKeyAuthenticationHandler(
        IOptionsMonitor<AuthenticationSchemeOptions> options,
        ILoggerFactory logger,
        UrlEncoder encoder,
        AuthKeys keys)
        : base(options, logger, encoder)
    {
        _keys = keys;
    }

    protected override Task<AuthenticateResult> HandleAuthenticateAsync()
    {
        if (!Request.Headers.TryGetValue(HeaderName, out var header)
            || string.IsNullOrWhiteSpace(header))
        {
            return Task.FromResult(AuthenticateResult.NoResult());
        }

        var key = header.ToString();

        if (key == _keys.Admin)
        {
            var identity = new ClaimsIdentity(
                new[] { new Claim(ClaimTypes.Role, "Admin") }, Scheme.Name);
            return Task.FromResult(AuthenticateResult.Success(
                new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
        }

        foreach (var (userId, userKey) in _keys.Users)
        {
            if (key == userKey)
            {
                var identity = new ClaimsIdentity(
                    new[]
                    {
                        new Claim(ClaimTypes.NameIdentifier, userId),
                        new Claim(ClaimTypes.Role, "User"),
                    },
                    Scheme.Name);
                return Task.FromResult(AuthenticateResult.Success(
                    new AuthenticationTicket(new ClaimsPrincipal(identity), Scheme.Name)));
            }
        }

        return Task.FromResult(AuthenticateResult.Fail("Invalid API key"));
    }

    protected override async Task HandleChallengeAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status401Unauthorized;
        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail("Unauthorized: missing or invalid API key"));
    }

    protected override async Task HandleForbiddenAsync(AuthenticationProperties properties)
    {
        Response.StatusCode = StatusCodes.Status403Forbidden;
        Response.ContentType = "application/json";
        await Response.WriteAsJsonAsync(
            ApiResponse<object>.Fail("Forbidden: insufficient permissions"));
    }
}