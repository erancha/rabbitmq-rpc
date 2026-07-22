using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using TodoApp.Shared.Models;
using TodoApp.Shared.Messages;

namespace TodoApp.WebApi.Controllers;

public abstract class BaseApiController : ControllerBase
{
    protected record LocalValidationResult(bool IsValid, string? ErrorMessage = null);

    private readonly ILogger<BaseApiController> _logger;

    protected BaseApiController(ILogger<BaseApiController> logger)
    {
        _logger = logger;
    }

    /// <summary>
    /// Converts a worker RPC response into an HTTP action result.
    /// Success: 200 OK with the Data payload, the CreatedId when present, or an empty body.
    /// Error: maps the RPC error kind to an HTTP status code (404 NOT_FOUND, 400 VALIDATION,
    /// 503 TEMPORARY_UNAVAILABLE, 500 otherwise) and returns { success: false, errorMessage }
    /// without exposing the internal error kind to HTTP clients.
    /// </summary>
    /// <param name="responseJson">The serialized RpcResponse received from the worker.</param>
    protected IActionResult HandleRpcResponse(string responseJson)
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

            if (genericResult.RootElement.TryGetProperty("Data", out var dataElement))
            {
                return Ok(dataElement.Deserialize<object>());
            }
            else if (genericResult.RootElement.TryGetProperty("CreatedId", out var createdIdElement) &&
                     !createdIdElement.ValueKind.Equals(JsonValueKind.Null))
            {
                return Ok(new { createdId = createdIdElement.GetInt32() });
            }

            return Ok();

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
            RpcErrorKind.TEMPORARY_UNAVAILABLE => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

    /// <summary>
    /// Converts controller-local validation into an HTTP response.
    /// If validation fails, returns <see cref="BadRequestObjectResult"/> with a simple payload.
    /// If validation succeeds, returns <c>null</c> so the caller can continue processing.
    /// </summary>
    /// <param name="validationResult">The result of local validation performed by the controller.</param>
    /// <returns>
    /// A <see cref="BadRequestObjectResult"/> when <paramref name="validationResult"/> is invalid; otherwise <c>null</c>.
    /// </returns>
    protected IActionResult HandleLocalResponse(LocalValidationResult validationResult)
    {
        if (!validationResult.IsValid)
        {
            var response = new { errorMessage = validationResult.ErrorMessage };
            return BadRequest(response);
        }
        return null!;
    }
}
