using System.Text.Json;
using VoidCapital.Api.Shared;

namespace VoidCapital.Api.Middleware;

/// <summary>
/// Global error handler. Maps known exceptions to HTTP status codes and always
/// returns the <see cref="ApiResponse{T}"/> envelope so callers never see raw
/// stack traces. Unhandled exceptions are logged in full and masked as a
/// generic 500.
/// </summary>
public class ExceptionMiddleware
{
    private readonly RequestDelegate _next;
    private readonly ILogger<ExceptionMiddleware> _logger;

    public ExceptionMiddleware(RequestDelegate next, ILogger<ExceptionMiddleware> logger)
    {
        _next = next;
        _logger = logger;
    }

    public async Task InvokeAsync(HttpContext context)
    {
        try
        {
            await _next(context);
        }
        catch (NotFoundException ex)
        {
            context.Response.StatusCode = StatusCodes.Status404NotFound;
            await WriteErrorAsync(context, "Not found", ex.Message);
        }
        catch (ValidationException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(context, "Validation failed", ex.Message);
        }
        catch (InsufficientFundsException ex)
        {
            context.Response.StatusCode = StatusCodes.Status400BadRequest;
            await WriteErrorAsync(context, "Insufficient funds", ex.Message);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Unhandled exception on {Method} {Path}",
                context.Request.Method, context.Request.Path);
            context.Response.StatusCode = StatusCodes.Status500InternalServerError;
            await WriteErrorAsync(context, "Internal server error", null);
        }
    }

    private static readonly JsonSerializerOptions JsonOptions = new(JsonSerializerDefaults.Web);

    private static async Task WriteErrorAsync(HttpContext context, string error, string? message)
    {
        context.Response.ContentType = "application/json";
        var response = ApiResponse<object>.Fail(
            string.IsNullOrWhiteSpace(message) ? error : $"{error}: {message}",
            context.TraceIdentifier);

        // Use web defaults (camelCase) so the error envelope matches the
        // success envelope produced by MVC's JsonSerializerOptions.
        await context.Response.WriteAsync(JsonSerializer.Serialize(response, JsonOptions));
    }
}
