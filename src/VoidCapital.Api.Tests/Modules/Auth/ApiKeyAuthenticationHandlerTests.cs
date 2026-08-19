using System.Security.Claims;
using System.Text.Encodings.Web;
using FluentAssertions;
using Microsoft.AspNetCore.Authentication;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using Moq;
using VoidCapital.Api.Modules.Auth.Models;
using VoidCapital.Api.Modules.Auth.Services;
using Xunit;

namespace VoidCapital.Api.Tests.Modules.Auth;

public class ApiKeyAuthenticationHandlerTests
{
    private static AuthKeys Keys() => new()
    {
        Admin = "admin-key",
        Users = new Dictionary<string, string>
        {
            ["1"] = "user1-key",
            ["2"] = "user2-key",
        },
    };

    private static IOptionsMonitor<AuthenticationSchemeOptions> OptionsMonitor()
    {
        var monitor = new Mock<IOptionsMonitor<AuthenticationSchemeOptions>>();
        monitor.SetupGet(o => o.CurrentValue).Returns(new AuthenticationSchemeOptions());
        monitor.Setup(o => o.Get(It.IsAny<string>())).Returns(new AuthenticationSchemeOptions());
        return monitor.Object;
    }

    private static TestableHandler CreateHandler(AuthKeys keys, string? headerValue)
    {
        var context = new DefaultHttpContext();
        if (headerValue is not null)
            context.Request.Headers[ApiKeyAuthenticationHandler.HeaderName] = headerValue;

        var handler = new TestableHandler(
            OptionsMonitor(),
            NullLoggerFactory.Instance,
            UrlEncoder.Default,
            keys);
        handler.InitializeAsync(
            new AuthenticationScheme(ApiKeyAuthenticationHandler.SchemeName, null, typeof(TestableHandler)),
            context).GetAwaiter().GetResult();
        return handler;
    }

    private sealed class TestableHandler : ApiKeyAuthenticationHandler
    {
        public TestableHandler(
            IOptionsMonitor<AuthenticationSchemeOptions> options,
            ILoggerFactory logger,
            UrlEncoder encoder,
            AuthKeys keys)
            : base(options, logger, encoder, keys)
        {
        }

        public Task<AuthenticateResult> Authenticate() => HandleAuthenticateAsync();
    }

    [Fact]
    public async Task AdminKey_YieldsAdminRole()
    {
        var handler = CreateHandler(Keys(), "admin-key");

        var result = await handler.Authenticate();

        result.Succeeded.Should().BeTrue();
        result.Principal!.IsInRole("Admin").Should().BeTrue();
    }

    [Fact]
    public async Task UserKey_YieldsUserRoleAndUserIdClaim()
    {
        var handler = CreateHandler(Keys(), "user2-key");

        var result = await handler.Authenticate();

        result.Succeeded.Should().BeTrue();
        result.Principal!.IsInRole("User").Should().BeTrue();
        result.Principal.FindFirstValue(ClaimTypes.NameIdentifier).Should().Be("2");
    }

    [Fact]
    public async Task MissingHeader_ReturnsNoResult()
    {
        var handler = CreateHandler(Keys(), null);

        var result = await handler.Authenticate();

        result.None.Should().BeTrue();
    }

    [Fact]
    public async Task InvalidKey_Fails()
    {
        var handler = CreateHandler(Keys(), "wrong-key");

        var result = await handler.Authenticate();

        result.Succeeded.Should().BeFalse();
        result.Failure.Should().NotBeNull();
    }
}