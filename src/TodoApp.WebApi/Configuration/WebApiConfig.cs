namespace TodoApp.WebApi.Configuration;

public class WebApiConfig
{
    /// <summary>
    /// Maximum time in seconds to wait for an RPC response before timing out.
    /// Can be configured via environment variable: WebApi__RpcTimeoutSeconds=<seconds>
    /// </summary>
    public int RpcTimeoutSeconds { get; set; } = 10;
}
