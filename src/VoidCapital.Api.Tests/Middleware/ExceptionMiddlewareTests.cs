using System.Text.Json;
using FluentAssertions;
using Microsoft.AspNetCore.Http;
using Microsoft.Extensions.Logging.Abstractions;
using VoidCapital.Api.Middleware;
using VoidCapital.Api.Shared;
using Xunit;

namespace VoidCapital.Api.Tests.Middleware;

public class ExceptionMiddlewareTests
{
    private static async Task<(int Status, string Body)> InvokeAsync(Func<HttpContext, Task> next)
    {
        var context = new DefaultHttpContext();
        context.TraceIdentifier = "trace-1";
        var bodyStream = new MemoryStream();
        context.Response.Body = bodyStream;

        var middleware = new ExceptionMiddleware(
            next: ctx => next(ctx),
            logger: NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        bodyStream.Position = 0;
        var body = await new StreamReader(bodyStream).ReadToEndAsync();
        return (context.Response.StatusCode, body);
    }

    [Fact]
    public async Task NoException_PassesThrough()
    {
        var (status, _) = await InvokeAsync(_ => Task.CompletedTask);
        status.Should().Be(StatusCodes.Status200OK);
    }

    [Fact]
    public async Task NotFoundException_MapsTo404()
    {
        var (status, body) = await InvokeAsync(_ =>
            throw new NotFoundException("no such holding"));

        status.Should().Be(StatusCodes.Status404NotFound);
        var envelope = JsonSerializer.Deserialize<ApiResponse<object>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        envelope!.Success.Should().BeFalse();
        envelope.Error.Should().Contain("Not found").And.Contain("no such holding");
        envelope.TraceId.Should().Be("trace-1");
    }

    [Fact]
    public async Task ValidationException_MapsTo400()
    {
        var (status, body) = await InvokeAsync(_ =>
            throw new ValidationException("quantity must be positive"));

        status.Should().Be(StatusCodes.Status400BadRequest);
        var envelope = JsonSerializer.Deserialize<ApiResponse<object>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        envelope!.Error.Should().Contain("Validation failed");
    }

    [Fact]
    public async Task InsufficientFundsException_MapsTo400()
    {
        var (status, body) = await InvokeAsync(_ =>
            throw new InsufficientFundsException("cash too low"));

        status.Should().Be(StatusCodes.Status400BadRequest);
        var envelope = JsonSerializer.Deserialize<ApiResponse<object>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        envelope!.Error.Should().Contain("Insufficient funds");
    }

    [Fact]
    public async Task UnknownException_MapsTo500_AndMasksDetails()
    {
        var (status, body) = await InvokeAsync(_ =>
            throw new InvalidOperationException("secret internal detail"));

        status.Should().Be(StatusCodes.Status500InternalServerError);
        var envelope = JsonSerializer.Deserialize<ApiResponse<object>>(body,
            new JsonSerializerOptions(JsonSerializerDefaults.Web));
        envelope!.Error.Should().Contain("Internal server error");
        // The raw exception message must never leak to the client.
        envelope.Error.Should().NotContain("secret internal detail");
    }

    [Fact]
    public async Task ResponseContentType_IsJson()
    {
        var context = new DefaultHttpContext();
        context.Response.Body = new MemoryStream();
        var middleware = new ExceptionMiddleware(
            _ => throw new NotFoundException("x"),
            NullLogger<ExceptionMiddleware>.Instance);

        await middleware.InvokeAsync(context);

        context.Response.ContentType.Should().Contain("application/json");
    }
}