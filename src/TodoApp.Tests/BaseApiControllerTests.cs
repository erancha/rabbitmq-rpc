using Xunit;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using TodoApp.Shared.Messages;
using TodoApp.WebApi.Controllers;
using TodoApp.WebApi.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the WebApi edge of the RPC pipeline: mapping of RPC error kinds to HTTP status
/// codes, conversion of worker response JSON into HTTP action results, resolution of the
/// idempotency key for write actions (supplied header, else derived from content), and the
/// shared publish-and-handle pipeline (ExecuteRpc) that owns publish, response handling, and
/// the catch-all 500 for every controller action.
/// </summary>
public class BaseApiControllerTests
{
    /// <summary>
    /// Records the arguments a controller passes to the RPC client and returns a canned response,
    /// or throws to exercise the publish-failure path, without touching a real broker.
    /// </summary>
    private sealed class FakeMessageService : IRabbitMQMessageService
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

    private sealed class TestableController : BaseApiController
    {
        public TestableController() : this(new FakeMessageService()) { }

        public TestableController(IRabbitMQMessageService messageService)
            : base(messageService, NullLogger<BaseApiController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        }

        public IActionResult InvokeHandleRpcResponse(string responseJson) => HandleRpcResponse(responseJson);
        public IActionResult InvokeHandleRpcCreatedResponse(string responseJson, string getActionName) =>
            HandleRpcCreatedResponse(responseJson, getActionName);
        public static int InvokeGetStatusCode(string? kind) => GetStatusCode(kind);
        public string InvokeResolveIdempotencyKey<T>(T message) => ResolveIdempotencyKey(message);

        public Task<IActionResult> InvokeExecuteRpc<TMessage>(
            TMessage message, string routingKey, bool executeIfTimeout, Func<string, IActionResult> onSuccess) =>
            ExecuteRpc(message, routingKey, executeIfTimeout, onSuccess);

        public void SetHeader(string name, string value) => HttpContext.Request.Headers[name] = value;
    }

    [Theory]
    [InlineData(RpcErrorKind.NOT_FOUND, 404)]
    [InlineData(RpcErrorKind.VALIDATION, 400)]
    [InlineData(RpcErrorKind.IDEMPOTENCY_CONFLICT, 422)]
    [InlineData(RpcErrorKind.TEMPORARY_UNAVAILABLE, 503)]
    [InlineData(RpcErrorKind.UNKNOWN, 500)]
    [InlineData(RpcErrorKind.FATAL, 500)]
    [InlineData(null, 500)]
    public void Error_kind_maps_to_http_status(string? kind, int expectedStatus)
    {
        Assert.Equal(expectedStatus, TestableController.InvokeGetStatusCode(kind));
    }

    [Fact]
    public void Supplied_idempotency_key_takes_precedence_over_content()
    {
        var controller = new TestableController();
        controller.SetHeader(BaseApiController.IdempotencyKeyHeader, "key-abc");

        Assert.Equal("key-abc", controller.InvokeResolveIdempotencyKey(new { title = "x", userId = 5 }));
    }

    [Theory]
    [InlineData("")]
    [InlineData("   ")]
    public void Absent_or_blank_key_is_derived_and_stable_for_identical_content(string headerValue)
    {
        var controller = new TestableController();
        controller.SetHeader(BaseApiController.IdempotencyKeyHeader, headerValue);

        var first = controller.InvokeResolveIdempotencyKey(new { title = "Buy milk", userId = 5 });
        var second = controller.InvokeResolveIdempotencyKey(new { title = "Buy milk", userId = 5 });

        Assert.False(string.IsNullOrEmpty(first));
        Assert.Equal(first, second);
    }

    [Fact]
    public void Derived_key_differs_for_different_content()
    {
        var controller = new TestableController();

        var first = controller.InvokeResolveIdempotencyKey(new { title = "Buy milk", userId = 5 });
        var second = controller.InvokeResolveIdempotencyKey(new { title = "Call dentist", userId = 5 });

        Assert.NotEqual(first, second);
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
    public void Bare_success_response_returns_ok_without_body()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcResponse("{\"Success\":true}");

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public void Created_response_returns_201_with_route_to_the_new_resource()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcCreatedResponse(
            "{\"Success\":true,\"Data\":{\"createdId\":7}}", "GetUserById");

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal("GetUserById", createdResult.ActionName);
        Assert.Equal(7, createdResult.RouteValues!["id"]);
        Assert.Contains("createdId", JsonSerializer.Serialize(createdResult.Value));
    }

    [Fact]
    public void Created_response_maps_worker_errors_like_any_other_response()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcCreatedResponse(
            $"{{\"Success\":false,\"Error\":{{\"Message\":\"bad input\",\"Kind\":\"{RpcErrorKind.VALIDATION}\"}}}}",
            "GetUserById");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(400, objectResult.StatusCode);
    }

    [Fact]
    public void Malformed_response_json_returns_500()
    {
        var controller = new TestableController();

        var result = controller.InvokeHandleRpcResponse("not json at all");

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task ExecuteRpc_passes_the_worker_response_to_onSuccess()
    {
        var service = new FakeMessageService { Response = "{\"Success\":true,\"Data\":{\"Username\":\"alice\"}}" };
        var controller = new TestableController(service);

        var result = await controller.InvokeExecuteRpc(
            new { name = "alice" }, "user", executeIfTimeout: false,
            onSuccess: controller.InvokeHandleRpcResponse);

        var okResult = Assert.IsType<OkObjectResult>(result);
        Assert.Contains("alice", JsonSerializer.Serialize(okResult.Value));
        Assert.Equal("user", service.CapturedRoutingKey);
    }

    [Fact]
    public async Task ExecuteRpc_maps_a_publish_failure_to_500()
    {
        var service = new FakeMessageService { PublishFailure = new InvalidOperationException("broker down") };
        var controller = new TestableController(service);

        var result = await controller.InvokeExecuteRpc(
            new { name = "alice" }, "user", executeIfTimeout: false,
            onSuccess: controller.InvokeHandleRpcResponse);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task ExecuteRpc_derives_an_idempotency_key_for_writes()
    {
        var service = new FakeMessageService();
        var controller = new TestableController(service);

        await controller.InvokeExecuteRpc(
            new { title = "Buy milk" }, "todo", executeIfTimeout: true,
            onSuccess: controller.InvokeHandleRpcResponse);

        Assert.True(service.CapturedExecuteIfTimeout);
        Assert.False(string.IsNullOrEmpty(service.CapturedIdempotencyKey));
    }

    [Fact]
    public async Task ExecuteRpc_sends_no_idempotency_key_for_reads()
    {
        var service = new FakeMessageService();
        var controller = new TestableController(service);

        await controller.InvokeExecuteRpc(
            new { id = 5 }, "todo", executeIfTimeout: false,
            onSuccess: controller.InvokeHandleRpcResponse);

        Assert.False(service.CapturedExecuteIfTimeout);
        Assert.Null(service.CapturedIdempotencyKey);
    }
}
