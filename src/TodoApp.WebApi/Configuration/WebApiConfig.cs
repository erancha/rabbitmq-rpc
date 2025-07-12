namespace TodoApp.WebApi.Configuration;

public class WebApiConfig
{
    /// <summary>
    /// Controls whether to log RPC request/response messages.
    /// Can be configured via environment variable: WebApi__EnableRequestLogging=true|false
    /// </summary>
    public bool EnableRequestLogging { get; set; } = false;

    /// <summary>
    /// Maximum time in seconds to wait for an RPC response before timing out.
    /// Can be configured via environment variable: WebApi__RpcTimeoutSeconds=<seconds>
    /// </summary>
    public int RpcTimeoutSeconds { get; set; } = 10;
}
