using TodoApp.WebApi.Services;

namespace TodoApp.Tests;

/// <summary>
/// Records the arguments a controller passes to the RPC client and returns a canned response,
/// or throws to exercise the publish-failure path, without touching a real broker.
/// </summary>
internal sealed class FakeMessageService : IRabbitMQMessageService
{
    public string Response = "{\"Success\":true}";
    public Exception? PublishFailure;

    public string? CapturedRoutingKey;
    public bool CapturedExecuteIfTimeout;
    public string? CapturedIdempotencyKey;

    public Task<string> PublishMessageRpc<T>(
        T message, string routingKey, bool executeIfTimeout = false, string? idempotencyKey = null)
    {
        CapturedRoutingKey = routingKey;
        CapturedExecuteIfTimeout = executeIfTimeout;
        CapturedIdempotencyKey = idempotencyKey;

        if (PublishFailure != null)
            throw PublishFailure;

        return Task.FromResult(Response);
    }
}
