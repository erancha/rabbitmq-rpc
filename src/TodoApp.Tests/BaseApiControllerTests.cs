using Xunit;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApp.Shared.Messages;
using TodoApp.WebApi.Controllers;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the WebApi edge of the RPC pipeline: mapping of RPC error kinds to HTTP status
/// codes and conversion of worker response JSON into HTTP action results.
/// </summary>
public class BaseApiControllerTests
{
    private sealed class TestableController : BaseApiController
    {
        public TestableController() : base(NullLogger<BaseApiController>.Instance) { }

        public IActionResult InvokeHandleRpcResponse(string responseJson) => HandleRpcResponse(responseJson);
        public static int InvokeGetStatusCode(string? kind) => GetStatusCode(kind);
    }

    [Theory]
    [InlineData(RpcErrorKind.NOT_FOUND, 404)]
    [InlineData(RpcErrorKind.VALIDATION, 400)]
    [InlineData(RpcErrorKind.TEMPORARY_UNAVAILABLE, 503)]
    [InlineData(RpcErrorKind.UNKNOWN, 500)]
    [InlineData(RpcErrorKind.FATAL, 500)]
    [InlineData(null, 500)]
    public void Error_kind_maps_to_http_status(string? kind, int expectedStatus)
    {
        Assert.Equal(expectedStatus, TestableController.InvokeGetStatusCode(kind));
    }

    [Fact]
    public void Error_response_produces_status_from_kind_and_exposes_message()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcResponse(
            $"{{\"Success\":false,\"Error\":{{\"Message\":\"user not found\",\"Kind\":\"{RpcErrorKind.NOT_FOUND}\"}}}}");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
        var payload = JsonSerializer.Serialize(objectResult.Value);
        Assert.Contains("user not found", payload);
        // The internal error kind must not leak to HTTP clients.
        Assert.DoesNotContain(RpcErrorKind.NOT_FOUND, payload);
    }

    [Fact]
    public void Success_response_with_data_returns_ok_with_the_data()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcResponse(
            "{\"Success\":true,\"Data\":{\"Id\":1,\"Username\":\"alice\"}}");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("alice", JsonSerializer.Serialize(okResult.Value));
    }

    [Fact]
    public void Success_response_with_created_id_returns_ok_with_created_id()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcResponse("{\"Success\":true,\"CreatedId\":42}");

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("42", JsonSerializer.Serialize(okResult.Value));
    }

    [Fact]
    public void Bare_success_response_returns_ok_without_body()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcResponse("{\"Success\":true}");

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public void Malformed_response_json_returns_500()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcResponse("not json at all");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }
}
