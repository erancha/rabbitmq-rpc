using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TodoApp.Shared.Messages;
using TodoApp.WebApi.Controllers;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.Tests;

/// <summary>
/// Verifies each user action's wiring into the typed RPC pipeline: routing key, read/write
/// flag, and the typed HTTP result produced from a worker-format reply.
/// </summary>
public class UsersControllerTests
{
    private static (UsersController Controller, FakeMessageService Service) CreateController(string response)
    {
        var service = new FakeMessageService { Response = response };
        var controller = new UsersController(service, NullLogger<UsersController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, service);
    }

    [Fact]
    public async Task GetAllUsers_reads_from_the_user_queue_and_returns_the_typed_list()
    {
        var (controller, service) = CreateController(
            "{\"Success\":true,\"Data\":{\"Users\":[{\"Id\":1,\"Username\":\"alice\",\"Email\":\"a@x.com\"}]}}");

        var result = await controller.GetAllUsers();

        Assert.Equal(RabbitMQShared.RoutingKeys.User, service.CapturedRoutingKey);
        Assert.False(service.CapturedExecuteIfTimeout);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        var payload = Assert.IsType<GetAllUsersResponse>(ok.Value);
        Assert.Equal("alice", Assert.Single(payload.Users!).Username);
    }

    [Fact]
    public async Task GetUserById_returns_the_typed_user()
    {
        var (controller, service) = CreateController(
            "{\"Success\":true,\"Data\":{\"User\":{\"Id\":1,\"Username\":\"alice\",\"Email\":\"a@x.com\"}}}");

        var result = await controller.GetUserById(1);

        Assert.False(service.CapturedExecuteIfTimeout);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("alice", Assert.IsType<GetUserByIdResponse>(ok.Value).User!.Username);
    }

    [Fact]
    public async Task CreateUser_writes_and_returns_201_with_the_created_id()
    {
        var (controller, service) = CreateController("{\"Success\":true,\"Data\":{\"createdId\":7}}");

        var result = await controller.CreateUser(new CreateUserMessage("alice", "a@x.com"));

        Assert.Equal(RabbitMQShared.RoutingKeys.User, service.CapturedRoutingKey);
        Assert.True(service.CapturedExecuteIfTimeout);
        Assert.False(string.IsNullOrEmpty(service.CapturedIdempotencyKey));
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(UsersController.GetUserById), created.ActionName);
        Assert.Equal(7, created.RouteValues!["id"]);
        Assert.Equal(7, Assert.IsType<CreatedResponse>(created.Value).CreatedId);
    }

    [Fact]
    public async Task UpdateUser_writes_and_returns_an_empty_200()
    {
        var (controller, service) = CreateController("{\"Data\":{},\"Success\":true}");

        var result = await controller.UpdateUser(1, new UpdateUserData { Username = "bob" });

        Assert.Equal(RabbitMQShared.RoutingKeys.User, service.CapturedRoutingKey);
        Assert.True(service.CapturedExecuteIfTimeout);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task DeleteUser_writes_and_returns_an_empty_200()
    {
        var (controller, service) = CreateController("{\"Data\":{},\"Success\":true}");

        var result = await controller.DeleteUser(3);

        Assert.True(service.CapturedExecuteIfTimeout);
        Assert.IsType<OkResult>(result);
    }
}
