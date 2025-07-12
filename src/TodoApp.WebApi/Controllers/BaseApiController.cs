using Microsoft.AspNetCore.Mvc;
using TodoApp.Shared.Models;

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
    /// For success responses, returns HTTP 200 with no content.
    /// For error responses:
    /// 1. Maps RPC error kinds to HTTP status codes (404 for NOT_FOUND, 400 for VALIDATION, 500 for others)
    /// 2. Creates a standardized error response that excludes internal error kinds
    /// </summary>
    /// <param name="result">The RPC response to handle</param>
    /// <returns>
    /// Success response: 200 OK with no content
    /// Error response: { success: false, errorMessage: string }
    /// </returns>
    protected IActionResult HandleRpcResponse(RpcResponse result)
    {
        _logger.LogInformation("Handling RPC response: {@Result}", result);

        if (result.Success)
            return result.CreatedId.HasValue
                ? Ok(new { createdId = result.CreatedId.Value })
                : Ok();

        // Map internal error kind to HTTP status code
        var statusCode = GetStatusCode(result.Error?.Kind);

        // Create standardized error response without exposing internal error kinds
        var response = new { errorMessage = result.Error?.Message };

        // _logger.LogWarning("Returning error response with {result}: {@Response}", result, response);
        return StatusCode(statusCode, response);
    }

    protected static int GetStatusCode(string? kind) =>
        kind switch
        {
            "NOT_FOUND" => StatusCodes.Status404NotFound,
            "VALIDATION" => StatusCodes.Status400BadRequest,
            "TEMPORARY_UNAVAILABLE" => StatusCodes.Status503ServiceUnavailable,
            _ => StatusCodes.Status500InternalServerError,
        };

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
