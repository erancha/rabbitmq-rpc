using System.Security.Cryptography;
using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TodoApp.Shared.Models;
using TodoApp.Shared.Messages;

namespace TodoApp.WebApi.Controllers;

public abstract class BaseApiController : ControllerBase
{
    // The HTTP header a caller may send to identify a write for deduplication; the same value the
    // worker contract uses, defined once as RpcHeaders.IdempotencyKey.
    public const string IdempotencyKeyHeader = RpcHeaders.IdempotencyKey;

    protected record LocalValidationResult(bool IsValid, string? ErrorMessage = null);

    private readonly ILogger<BaseApiController> _logger;

    protected BaseApiController(ILogger<BaseApiController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Converts a worker RPC response into an HTTP action result.
    /// Success: 200 OK with the Data payload, or an empty body.
    /// Error: maps the RPC error kind to an HTTP status code (404 NOT_FOUND, 400 VALIDATION,
    /// 422 IDEMPOTENCY_CONFLICT, 503 TEMPORARY_UNAVAILABLE, 500 otherwise) and returns
    /// { success: false, errorMessage } without exposing the internal error kind to HTTP clients.
    /// </summary>
    /// <param name="responseJson">The serialized RpcResponse received from the worker.</param>
    protected IActionResult HandleRpcResponse(string responseJson) =>
        HandleRpcResponse(responseJson, root =>
            root.TryGetProperty("Data", out var dataElement)
                ? Ok(dataElement.Deserialize<object>())
                : Ok());

    /// <summary>
    /// Converts a worker RPC response to a creation into an HTTP action result.
    /// Success: 201 Created with the Data payload and a Location header pointing at the
    /// resource's GET action, using the createdId the worker returns as the route id.
    /// Errors are mapped exactly as in HandleRpcResponse; a success payload without createdId
    /// violates the creation contract and surfaces as a 500 rather than being defaulted.
    /// </summary>
    /// <param name="responseJson">The serialized RpcResponse received from the worker.</param>
    /// <param name="getActionName">Name of the controller's GET-by-id action for the Location header.</param>
    protected IActionResult HandleRpcCreatedResponse(string responseJson, string getActionName) =>
        HandleRpcResponse(responseJson, root =>
        {
            var dataElement = root.GetProperty("Data");
            var createdId = dataElement.GetProperty("createdId").GetInt32();
            return CreatedAtAction(getActionName, new { id = createdId }, dataElement.Deserialize<object>());
        });

    /// <summary>
    /// Shared RPC response pipeline: parses the envelope, maps worker errors to HTTP status
    /// codes without exposing the internal error kind, and delegates successful envelopes to
    /// the caller's result factory. Any parsing or factory failure becomes a 500.
    /// </summary>
    private IActionResult HandleRpcResponse(string responseJson, Func<JsonElement, IActionResult> onSuccess)
    {
        _logger.LogInformation("Handling RPC response: {Response}", responseJson);

        try
        {
            var genericResult = JsonDocument.Parse(responseJson);
            var isSuccess = genericResult.RootElement.GetProperty("Success").GetBoolean();

            if (!isSuccess)
            {
                var error = genericResult.RootElement.GetProperty("Error").Deserialize<RpcError>();
                var statusCode = GetStatusCode(error?.Kind);
                return StatusCode(statusCode, new { success = false, errorMessage = error?.Message });
            }

            return onSuccess(genericResult.RootElement);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error handling RPC response");
            return StatusCode(500, new { success = false, errorMessage = "Error processing response" });
        }
    }

    protected static int GetStatusCode(string? kind) =>
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
    /// Returns a 400 Bad Request result carrying the validation error message when
    /// controller-local validation failed, or null when it passed — null tells the action
    /// to proceed and publish the RPC message.
    /// </summary>
    protected IActionResult? HandleLocalResponse(LocalValidationResult validationResult)
    {
        if (!validationResult.IsValid)
        {
            var response = new { errorMessage = validationResult.ErrorMessage };
            return BadRequest(response);
        }
        return null;
    }
}
