using FluentAssertions;
using VoidCapital.Api.Modules.Auth.Models;
using Xunit;

namespace VoidCapital.Api.Tests.Modules.Auth.Models;

public class AuthKeysTests
{
    private static string WriteTempFile(string content)
    {
        var path = Path.Combine(Path.GetTempPath(), $"authkeys-{Guid.NewGuid():N}.json");
        File.WriteAllText(path, content);
        return path;
    }

    [Fact]
    public void Load_ValidFile_ReturnsKeys()
    {
        var path = WriteTempFile("""
            {
              "admin": "admin-secret",
              "users": { "1": "user-one", "2": "user-two" }
            }
            """);
        try
        {
            var keys = AuthKeys.Load(path);

            keys.Admin.Should().Be("admin-secret");
            keys.Users.Should().Contain("1", "user-one");
            keys.Users.Should().Contain("2", "user-two");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_PropertyNamesAreCaseInsensitive()
    {
        var path = WriteTempFile("""
            { "ADMIN": "admin-secret", "USERS": { "1": "user-one" } }
            """);
        try
        {
            var keys = AuthKeys.Load(path);

            keys.Admin.Should().Be("admin-secret");
            keys.Users.Should().Contain("1", "user-one");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingFile_ThrowsWithPath()
    {
        var missing = Path.Combine(Path.GetTempPath(), $"authkeys-{Guid.NewGuid():N}.json");

        var act = () => AuthKeys.Load(missing);

        act.Should().Throw<InvalidOperationException>()
            .WithMessage($"*not found at '{missing}'*");
    }

    [Fact]
    public void Load_InvalidJson_ThrowsJsonException()
    {
        // Malformed JSON surfaces as JsonException from the deserializer; the
        // friendly "empty or invalid JSON" message only fires for literal null.
        var path = WriteTempFile("{ not json");
        try
        {
            var act = () => AuthKeys.Load(path);

            act.Should().Throw<System.Text.Json.JsonException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_EmptyFile_ThrowsJsonException()
    {
        var path = WriteTempFile("");
        try
        {
            var act = () => AuthKeys.Load(path);

            act.Should().Throw<System.Text.Json.JsonException>();
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_LiteralNullJson_ThrowsWithFriendlyMessage()
    {
        var path = WriteTempFile("null");
        try
        {
            var act = () => AuthKeys.Load(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*empty or invalid JSON*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_MissingAdminKey_Throws()
    {
        var path = WriteTempFile("""{ "users": { "1": "user-one" } }""");
        try
        {
            var act = () => AuthKeys.Load(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*missing the 'admin' key*");
        }
        finally
        {
            File.Delete(path);
        }
    }

    [Fact]
    public void Load_BlankAdminKey_Throws()
    {
        var path = WriteTempFile("""{ "admin": "   ", "users": {} }""");
        try
        {
            var act = () => AuthKeys.Load(path);

            act.Should().Throw<InvalidOperationException>()
                .WithMessage("*missing the 'admin' key*");
        }
        finally
        {
            File.Delete(path);
        }
    }
}