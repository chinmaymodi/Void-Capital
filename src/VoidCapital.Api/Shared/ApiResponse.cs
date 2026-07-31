namespace VoidCapital.Api.Shared;

/// <summary>
/// Standard API response envelope. Every endpoint returns this shape so the
/// frontend can rely on a consistent contract: Success + Data XOR Error.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; set; }
    public T? Data { get; set; }
    public string? Error { get; set; }
    public string? TraceId { get; set; }

    public static ApiResponse<T> Ok(T data) => new() { Success = true, Data = data };

    public static ApiResponse<T> Fail(string error, string? traceId = null) =>
        new() { Success = false, Error = error, TraceId = traceId };
}
