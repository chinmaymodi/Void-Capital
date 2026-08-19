using System.Security.Claims;
using FluentAssertions;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Shared;

public class ClaimsPrincipalExtensionsTests
{
    private static ClaimsPrincipal Admin() =>
        new(new ClaimsIdentity(new[] { new Claim(ClaimTypes.Role, "Admin") }, "test"));

    private static ClaimsPrincipal User(int id) =>
        new(new ClaimsIdentity(
            new[]
            {
                new Claim(ClaimTypes.NameIdentifier, id.ToString()),
                new Claim(ClaimTypes.Role, "User"),
            },
            "test"));

    [Fact]
    public void Admin_CanAccessAnyUser() =>
        Admin().CanAccess(5).Should().BeTrue();

    [Fact]
    public void User_CanAccessOwnId() =>
        User(3).CanAccess(3).Should().BeTrue();

    [Fact]
    public void User_CannotAccessOtherId() =>
        User(3).CanAccess(4).Should().BeFalse();

    [Fact]
    public void Admin_IsAdmin() =>
        Admin().IsAdmin().Should().BeTrue();

    [Fact]
    public void User_IsNotAdmin() =>
        User(1).IsAdmin().Should().BeFalse();
}