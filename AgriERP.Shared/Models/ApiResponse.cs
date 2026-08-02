namespace AgriERP.Shared.Models;

/// <summary>
/// Uniform response envelope. Every endpoint returns this shape, success or
/// failure, so the frontend has one place to check <see cref="Success"/> and
/// one place to read errors - rather than guessing whether a 400 carried a
/// ProblemDetails, a bare string, or a validation dictionary.
/// </summary>
public class ApiResponse<T>
{
    public bool Success { get; init; }
    public string? Message { get; init; }
    public T? Data { get; init; }

    /// <summary>Field name -> messages. Populated for validation failures only.</summary>
    public IDictionary<string, string[]>? Errors { get; init; }

    /// <summary>Correlates a failure with the server log entry.</summary>
    public string? TraceId { get; init; }

    public static ApiResponse<T> Ok(T data, string? message = null)
        => new() { Success = true, Data = data, Message = message };

    public static ApiResponse<T> Fail(string message, IDictionary<string, string[]>? errors = null, string? traceId = null)
        => new() { Success = false, Message = message, Errors = errors, TraceId = traceId };
}

/// <summary>Non-generic form for endpoints that return no payload.</summary>
public class ApiResponse : ApiResponse<object>
{
    public static ApiResponse Ok(string? message = null)
        => new() { Success = true, Message = message };

    public new static ApiResponse Fail(string message, IDictionary<string, string[]>? errors = null, string? traceId = null)
        => new() { Success = false, Message = message, Errors = errors, TraceId = traceId };
}
