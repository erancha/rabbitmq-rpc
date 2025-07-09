namespace TodoApp.Shared.Models;

public class RpcResponse
{
    public bool Success { get; set; }
    public int? CreatedId { get; set; }
    public RpcError? Error { get; set; }
}

public class RpcError
{
    public string Message { get; set; } = string.Empty;
    public string Kind { get; set; } = string.Empty;
}
