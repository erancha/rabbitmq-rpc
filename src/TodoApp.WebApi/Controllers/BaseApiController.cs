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
    /// Handles RPC responses by converting them to appropriate HTTP responses.
    /// For success responses:
    /// For error responses:
    /// 1. Maps RPC error kinds to HTTP status codes (404 for NOT_FOUND, 400 for VALIDATION, 500 for others)
    /// 2. Creates a standardized error response that excludes internal error kinds
    /// </summary>
    /// <param name="result">The RPC response to handle</param>
    /// <returns>
    /// Success response: 200 OK with data if present, createdId if available, or no content
    /// Error response: { success: false, errorMessage: string }
    /// </returns>
    protected IActionResult HandleRpcResponse(string responseJson)
    {
        _logger.LogInformation("Handling RPC response: {Response}", responseJson);

        try
        {
            // Try to deserialize as generic response first
            var genericResult = JsonDocument.Parse(responseJson);
            var isSuccess = genericResult.RootElement.GetProperty("Success").GetBoolean();
            
            if (!isSuccess)
            {
                // Handle error response
                var error = genericResult.RootElement.GetProperty("Error").Deserialize<RpcError>();
                var statusCode = GetStatusCode(error?.Kind);
                return StatusCode(statusCode, new { success = false, errorMessage = error?.Message });
            }

            // Handle success response
            if (genericResult.RootElement.TryGetProperty("Data", out var dataElement))
            {
                // Return the data if it exists
                return Ok(dataElement.Deserialize<object>());
            }
            else if (genericResult.RootElement.TryGetProperty("CreatedId", out var createdIdElement) && 
                     !createdIdElement.ValueKind.Equals(JsonValueKind.Null))
            {
                // Return createdId if it exists and is not null
                return Ok(new { createdId = createdIdElement.GetInt32() });
            }
            
            // Return empty success response
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
            "NOT_FOUND" => StatusCodes.Status404NotFound,
            "VALIDATION" => StatusCodes.Status400BadRequest,
            "TEMPORARY_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
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
