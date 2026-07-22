namespace TodoApp.Shared.Messages;

public class RpcResponse
{
    public bool Success { get; set; }
    public int? CreatedId { get; set; }
    public RpcError? Error { get; set; }

    public override string ToString()
    {
        if (Success)
            return CreatedId.HasValue ? $"Success (ID: {CreatedId})" : "Success";
        return Error != null ? $"Error: {Error.Message} ({Error.Kind})" : "Error: Unknown";
    }
}

public class RpcResponse<T> : RpcResponse
{
    public T? Data { get; set; }

    public override string ToString()
    {
        if (Success && Data != null)
            return $"Success: {Data}";
        return base.ToString();
    }
}

public class RpcError
{
    public string Message { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}

/// <summary>
/// Error kinds carried in RPC responses. Produced by the worker and the WebApi timeout path,
/// consumed by the WebApi when mapping responses to HTTP status codes.
/// </summary>
public static class RpcErrorKind
{
    public const string NOT_FOUND = "NOT_FOUND";
    public const string VALIDATION = "VALIDATION";
    public const string UNKNOWN = "UNKNOWN";
    public const string FATAL = "FATAL";
    public const string TEMPORARY_UNAVAILABLE = "TEMPORARY_UNAVAILABLE";
}

/// <summary>
/// AMQP header names of the RPC request contract between the WebApi publisher and the worker.
/// </summary>
public static class RpcHeaders
{
    public const string TimeoutSeconds = "timeout_seconds";
    public const string ExecuteIfTimeout = "execute_if_timeout";
}
