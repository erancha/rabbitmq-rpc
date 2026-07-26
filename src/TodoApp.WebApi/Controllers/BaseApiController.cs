using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TodoApp.Shared.Messages;
using TodoApp.WebApi.Services;

namespace TodoApp.WebApi.Controllers;

public abstract class BaseApiController : ControllerBase
{
    // The HTTP header a caller may send to identify a write for deduplication; the same value the
    // worker contract uses, defined once as RpcHeaders.IdempotencyKey.
    public const string IdempotencyKeyHeader = RpcHeaders.IdempotencyKey;

    protected record LocalValidationResult(bool IsValid, string? ErrorMessage = null);

    private readonly IRabbitMQMessageService _rabbitMQMessageService;
    private readonly ILogger<BaseApiController> _logger;

    protected BaseApiController(IRabbitMQMessageService rabbitMQMessageService, ILogger<BaseApiController> logger)
    {
        _rabbitMQMessageService = rabbitMQMessageService;
        _logger = logger;
    }

    // Reads worker transport JSON. Web defaults match the transport's PascalCase properties
    // case-insensitively, so the internal wire format needs no change.
    private static readonly JsonSerializerOptions TransportJsonOptions = new(JsonSerializerDefaults.Web);

    /// <summary>
    /// Publishes the RPC message and converts the worker's typed reply into an HTTP result.
    /// Success hands the deserialized payload to onSuccess (default: 200 OK with the payload);
    /// worker errors map their kind to an HTTP status carrying a ProblemDetails body. A success
    /// reply without its payload violates the worker contract and surfaces as a 500.
    ///
    /// executeIfTimeout marks a state-changing write: writes carry it true and are deduplicated
    /// under an idempotency key resolved here (caller header, else derived from content); reads
    /// pass it false and carry no key.
    /// </summary>
    protected Task<ActionResult> ExecuteRpc<TMessage, TData>(
        TMessage message,
        string routingKey,
        bool executeIfTimeout,
        Func<TData, ActionResult>? onSuccess = null)
        where TData : class =>
        ExecuteRpcCore(message, routingKey, executeIfTimeout, responseJson =>
        {
            var rpc = DeserializeReply<RpcResponse<TData>>(responseJson);
            if (!rpc.Success)
                return RpcErrorResult(rpc.Error);

            var data = rpc.Data ?? throw new JsonException(
                $"Success reply carried no {typeof(TData).Name} payload");

            return onSuccess != null ? onSuccess(data) : Ok(data);
        });

    /// <summary>
    /// Variant for actions whose success carries no payload (updates and deletes): returns an
    /// empty 200 on success and maps worker errors exactly like the typed overload.
    /// </summary>
    protected Task<ActionResult> ExecuteRpc<TMessage>(
        TMessage message,
        string routingKey,
        bool executeIfTimeout) =>
        ExecuteRpcCore(message, routingKey, executeIfTimeout, responseJson =>
        {
            var rpc = DeserializeReply<RpcResponse>(responseJson);
            return rpc.Success ? Ok() : RpcErrorResult(rpc.Error);
        });

    /// <summary>
    /// Shared publish skeleton: resolves the idempotency key for writes, publishes, and hands the
    /// raw reply to onReply. Any failure — publish, deserialization, or a contract violation
    /// thrown by onReply — becomes a 500 ProblemDetails.
    /// </summary>
    private async Task<ActionResult> ExecuteRpcCore<TMessage>(
        TMessage message,
        string routingKey,
        bool executeIfTimeout,
        Func<string, ActionResult> onReply)
    {
        var idempotencyKey = executeIfTimeout ? ResolveIdempotencyKey(message) : null;

        try
        {
            var responseJson = await _rabbitMQMessageService.PublishMessageRpc(
                message,
                routingKey,
                executeIfTimeout: executeIfTimeout,
                idempotencyKey: idempotencyKey
            );
            return onReply(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error executing {MessageType} RPC", typeof(TMessage).Name);
            return Problem(statusCode: StatusCodes.Status500InternalServerError, detail: "Error processing request");
        }
    }

    private static T DeserializeReply<T>(string responseJson) =>
        JsonSerializer.Deserialize<T>(responseJson, TransportJsonOptions)
            ?? throw new JsonException("Worker reply deserialized to null");

    /// <summary>
    /// Maps a worker error to its HTTP status with a ProblemDetails body. The internal error kind
    /// selects the status but is not exposed; only the domain-authored message travels as detail.
    /// </summary>
    private ObjectResult RpcErrorResult(RpcError? error)
    {
        var rpcError = error ?? throw new JsonException("Failed reply carried no Error");
        return Problem(statusCode: GetStatusCode(rpcError.Kind), detail: rpcError.Message);
    }

    protected static int GetStatusCode(string kind) =>
        kind switch
        {
            RpcErrorKind.NOT_FOUND => StatusCodes.Status404NotFound,
            RpcErrorKind.VALIDATION => StatusCodes.Status400BadRequest,
            RpcErrorKind.IDEMPOTENCY_CONFLICT => StatusCodes.Status422UnprocessableEntity,
            RpcErrorKind.TEMPORARY_UNAVAILABLE => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

    /// <summary>
    /// Resolves the idempotency key for a write. Uses the caller-supplied Idempotency-Key header
    /// when present; otherwise derives a stable key by hashing the operation's content (message type
    /// plus serialized body), so keyless retries and broker redeliveries still deduplicate.
    ///
    /// A derived key collapses byte-identical requests into one within the marker retention window —
    /// intended for a retry, but it cannot tell a retry from a caller deliberately repeating the same
    /// write. A caller that needs two identical writes kept distinct supplies its own distinct keys.
    /// </summary>
    protected string ResolveIdempotencyKey<T>(T message)
    {
        var supplied = Request.Headers[IdempotencyKeyHeader].ToString();
        if (!string.IsNullOrWhiteSpace(supplied))
            return supplied;

        var content = $"{typeof(T).Name}:{JsonSerializer.Serialize(message)}";
        return Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
    }

    /// <summary>
    /// Returns a 400 ProblemDetails result when controller-local validation failed, or null when
    /// it passed — null tells the action to proceed and publish the RPC message.
    /// </summary>
    protected ActionResult? HandleLocalResponse(LocalValidationResult validationResult)
    {
        if (!validationResult.IsValid)
            return Problem(statusCode: StatusCodes.Status400BadRequest, detail: validationResult.ErrorMessage);
        return null;
    }
}
