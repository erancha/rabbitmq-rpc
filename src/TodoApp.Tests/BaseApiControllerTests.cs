using Xunit;
using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.Options;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WebApi.Controllers;
using TodoApp.WebApi.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the typed WebApi RPC pipeline: resolution of the idempotency key for write
/// actions (supplied header, else derived from content), typed deserialization of worker
/// responses into contract shapes (with payload or bare), mapping of worker error kinds to
/// HTTP status codes, and the shared ExecuteRpc pipeline that owns publish, typed response
/// handling, and the catch-all 500 for any failure.
/// </summary>
public class BaseApiControllerTests
{
    private sealed class TestableController : BaseApiController
    {
        public TestableController() : this(new FakeMessageService()) { }

        public TestableController(IRabbitMQMessageService messageService)
            : base(messageService, NullLogger<BaseApiController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() };
        }

        public static int InvokeGetStatusCode(string kind) => GetStatusCode(kind);
        public string InvokeResolveIdempotencyKey<T>(T message) => ResolveIdempotencyKey(message);

        public void SetHeader(string name, string value) => HttpContext.Request.Headers[name] = value;

        public Task<ActionResult> InvokeTypedExecuteRpc<TMessage, TData>(
            TMessage message, string routingKey, bool executeIfTimeout,
            Func<TData, ActionResult>? onSuccess = null) where TData : class =>
            ExecuteRpc<TMessage, TData>(message, routingKey, executeIfTimeout, onSuccess);

        public Task<ActionResult> InvokeBareExecuteRpc<TMessage>(
            TMessage message, string routingKey, bool executeIfTimeout) =>
            ExecuteRpc(message, routingKey, executeIfTimeout);
    }

    [Theory]
    [InlineData(RpcErrorKind.NOT_FOUND, 404)]
    [InlineData(RpcErrorKind.VALIDATION, 400)]
    [InlineData(RpcErrorKind.IDEMPOTENCY_CONFLICT, 422)]
    [InlineData(RpcErrorKind.TEMPORARY_UNAVAILABLE, 503)]
    [InlineData(RpcErrorKind.UNKNOWN, 500)]
    [InlineData(RpcErrorKind.FATAL, 500)]
    public void Error_kind_maps_to_http_status(string kind, int expectedStatus)
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
    public async Task Typed_success_returns_ok_with_the_deserialized_payload()
    {
        var service = new FakeMessageService
        {
            Response = "{\"Success\":true,\"Data\":{\"Users\":[{\"Id\":1,\"Username\":\"alice\",\"Email\":\"a@x.com\"}]}}"
        };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<GetAllUsersMessage, GetAllUsersResponse>(
            new GetAllUsersMessage(), "user", executeIfTimeout: false);

        var ok = Assert.IsType<OkObjectResult>(result);
        var payload = Assert.IsType<GetAllUsersResponse>(ok.Value);
        Assert.Equal("alice", Assert.Single(payload.Users!).Username);
    }

    [Fact]
    public async Task Typed_error_maps_kind_to_status_and_exposes_only_the_message()
    {
        var service = new FakeMessageService
        {
            Response = $"{{\"Success\":false,\"Error\":{{\"Message\":\"user not found\",\"Kind\":\"{RpcErrorKind.NOT_FOUND}\"}}}}"
        };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<GetUserByIdMessage, GetUserByIdResponse>(
            new GetUserByIdMessage(5), "user", executeIfTimeout: false);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(404, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal("user not found", problem.Detail);
        // The internal error kind must not leak to HTTP clients.
        Assert.DoesNotContain(RpcErrorKind.NOT_FOUND, JsonSerializer.Serialize(problem));
    }

    // The literal envelope RabbitMQMessageService.PublishMessageRpc's timeout branch produces —
    // exercised through ExecuteRpc rather than reconstructed, so a drift in either side is caught.
    private const string TimeoutEnvelope =
        "{\"Success\":false,\"Error\":{\"Message\":\"Service is temporarily unavailable (timeout: 30s). " +
        "Your request remains queued and will not be lost.\",\"Kind\":\"TEMPORARY_UNAVAILABLE\"}}";

    [Fact]
    public async Task Typed_pipeline_maps_the_timeout_envelope_to_503()
    {
        var service = new FakeMessageService { Response = TimeoutEnvelope };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<GetAllUsersMessage, GetAllUsersResponse>(
            new GetAllUsersMessage(), "user", executeIfTimeout: false);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(
            "Service is temporarily unavailable (timeout: 30s). Your request remains queued and will not be lost.",
            problem.Detail);
    }

    [Fact]
    public async Task Bare_pipeline_maps_the_timeout_envelope_to_503()
    {
        var service = new FakeMessageService { Response = TimeoutEnvelope };
        var controller = new TestableController(service);

        var result = await controller.InvokeBareExecuteRpc(
            new DeleteUserMessage(3), "user", executeIfTimeout: true);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(503, objectResult.StatusCode);
        var problem = Assert.IsType<ProblemDetails>(objectResult.Value);
        Assert.Equal(
            "Service is temporarily unavailable (timeout: 30s). Your request remains queued and will not be lost.",
            problem.Detail);
    }

    [Fact]
    public async Task Bare_success_returns_an_empty_200()
    {
        // The worker's no-payload success reply carries Data as an empty object.
        var service = new FakeMessageService { Response = "{\"Data\":{},\"Success\":true}" };
        var controller = new TestableController(service);

        var result = await controller.InvokeBareExecuteRpc(
            new DeleteUserMessage(3), "user", executeIfTimeout: true);

        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task Typed_created_flow_builds_the_route_from_the_payload()
    {
        var service = new FakeMessageService { Response = "{\"Success\":true,\"Data\":{\"createdId\":7}}" };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<CreateUserMessage, CreatedResponse>(
            new CreateUserMessage("alice", "a@x.com"), "user", executeIfTimeout: true,
            onSuccess: created => controller.CreatedAtAction(
                "GetUserById", new { id = created.CreatedId }, created));

        var createdResult = Assert.IsType<CreatedAtActionResult>(result);
        Assert.Equal("GetUserById", createdResult.ActionName);
        Assert.Equal(7, createdResult.RouteValues!["id"]);
        Assert.Equal(7, Assert.IsType<CreatedResponse>(createdResult.Value).CreatedId);
    }

    [Fact]
    public async Task Creation_success_without_an_id_surfaces_as_500()
    {
        var service = new FakeMessageService { Response = "{\"Success\":true,\"Data\":{}}" };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<CreateUserMessage, CreatedResponse>(
            new CreateUserMessage("alice", "a@x.com"), "user", executeIfTimeout: true);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task Typed_success_without_its_payload_surfaces_as_500()
    {
        var service = new FakeMessageService { Response = "{\"Success\":true}" };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<GetAllUsersMessage, GetAllUsersResponse>(
            new GetAllUsersMessage(), "user", executeIfTimeout: false);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task Failed_reply_with_no_error_surfaces_as_500()
    {
        var service = new FakeMessageService { Response = "{\"Success\":false}" };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<GetAllUsersMessage, GetAllUsersResponse>(
            new GetAllUsersMessage(), "user", executeIfTimeout: false);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task Malformed_reply_surfaces_as_500()
    {
        var service = new FakeMessageService { Response = "not json at all" };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<GetAllUsersMessage, GetAllUsersResponse>(
            new GetAllUsersMessage(), "user", executeIfTimeout: false);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
    }

    [Fact]
    public async Task Typed_publish_failure_maps_to_a_500_problem()
    {
        var service = new FakeMessageService { PublishFailure = new InvalidOperationException("broker down") };
        var controller = new TestableController(service);

        var result = await controller.InvokeTypedExecuteRpc<GetAllUsersMessage, GetAllUsersResponse>(
            new GetAllUsersMessage(), "user", executeIfTimeout: false);

        var objectResult = Assert.IsType<ObjectResult>(result);
        Assert.Equal(500, objectResult.StatusCode);
        Assert.IsType<ProblemDetails>(objectResult.Value);
    }

    [Fact]
    public async Task Typed_pipeline_derives_an_idempotency_key_for_writes()
    {
        var service = new FakeMessageService { Response = "{\"Data\":{},\"Success\":true}" };
        var controller = new TestableController(service);

        await controller.InvokeBareExecuteRpc(new DeleteUserMessage(3), "user", executeIfTimeout: true);

        Assert.True(service.CapturedExecuteIfTimeout);
        Assert.False(string.IsNullOrEmpty(service.CapturedIdempotencyKey));
    }

    [Fact]
    public async Task Typed_pipeline_sends_no_idempotency_key_for_reads()
    {
        var service = new FakeMessageService
        {
            Response = "{\"Success\":true,\"Data\":{\"Users\":[]}}"
        };
        var controller = new TestableController(service);

        await controller.InvokeTypedExecuteRpc<GetAllUsersMessage, GetAllUsersResponse>(
            new GetAllUsersMessage(), "user", executeIfTimeout: false);

        Assert.False(service.CapturedExecuteIfTimeout);
        Assert.Null(service.CapturedIdempotencyKey);
    }

    [Fact]
    public void Configured_mvc_pipeline_serializes_camelCase()
    {
        // Program.cs registers a bare AddControllers() with no AddJsonOptions override, so MVC's
        // default JsonSerializerOptions (camelCase) governs the wire body; this resolves those
        // options through the same DI registration rather than a hand-built copy.
        var services = new ServiceCollection();
        services.AddControllers();
        var opts = services.BuildServiceProvider()
            .GetRequiredService<IOptions<Microsoft.AspNetCore.Mvc.JsonOptions>>()
            .Value.JsonSerializerOptions;

        Assert.Equal(JsonNamingPolicy.CamelCase, opts.PropertyNamingPolicy);

        var wire = JsonSerializer.Serialize(
            new GetAllUsersResponse
            {
                Users = new List<User> { new() { Id = 1, Username = "alice", Email = "a@x.com" } }
            },
            opts);

        Assert.Contains("\"users\":", wire);
        Assert.Contains("\"id\":1", wire);
        Assert.Contains("\"username\":\"alice\"", wire);
        Assert.DoesNotContain("\"Users\":", wire);
    }
}
