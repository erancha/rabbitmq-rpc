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

public static class RpcErrorKind
{
    public const string NOT_FOUND = "NOT_FOUND";
    public const string VALIDATION = "VALIDATION";
    public const string UNKNOWN = "UNKNOWN";
}
