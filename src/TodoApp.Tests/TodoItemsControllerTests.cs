using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Xunit;
using TodoApp.Shared.Messages;
using TodoApp.WebApi.Controllers;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.Tests;

/// <summary>
/// Verifies each todo action's wiring into the typed RPC pipeline: routing key, read/write
/// flag, and the typed HTTP result produced from a worker-format reply.
/// </summary>
public class TodoItemsControllerTests
{
    private static (TodoItemsController Controller, FakeMessageService Service) CreateController(string response)
    {
        var service = new FakeMessageService { Response = response };
        var controller = new TodoItemsController(service, NullLogger<TodoItemsController>.Instance)
        {
            ControllerContext = new ControllerContext { HttpContext = new DefaultHttpContext() }
        };
        return (controller, service);
    }

    [Fact]
    public async Task CreateTodoItem_writes_and_returns_201_with_the_created_id()
    {
        var (controller, service) = CreateController("{\"Success\":true,\"Data\":{\"createdId\":9}}");

        var result = await controller.CreateTodoItem(new CreateTodoItemMessage("Buy milk", "2%", 5));

        Assert.Equal(RabbitMQShared.RoutingKeys.Todo, service.CapturedRoutingKey);
        Assert.True(service.CapturedExecuteIfTimeout);
        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(TodoItemsController.GetTodoItemById), created.ActionName);
        Assert.Equal(9, created.RouteValues!["id"]);
    }

    [Fact]
    public async Task CreateTodoItem_rejects_a_missing_title_locally_with_a_problem_body()
    {
        var (controller, service) = CreateController("{\"Success\":true}");

        var result = await controller.CreateTodoItem(new CreateTodoItemMessage("", "d", 5));

        Assert.Null(service.CapturedRoutingKey); // rejected before any publish
        var objectResult = Assert.IsType<ObjectResult>(result.Result);
        Assert.Equal(400, objectResult.StatusCode);
        Assert.Equal("Title cannot be empty", Assert.IsType<ProblemDetails>(objectResult.Value).Detail);
    }

    [Fact]
    public async Task UpdateTodoItem_writes_and_returns_an_empty_200()
    {
        var (controller, service) = CreateController("{\"Data\":{},\"Success\":true}");

        var result = await controller.UpdateTodoItem(1, new UpdateTodoItemData { IsCompleted = true });

        Assert.Equal(RabbitMQShared.RoutingKeys.Todo, service.CapturedRoutingKey);
        Assert.True(service.CapturedExecuteIfTimeout);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task DeleteTodoItem_writes_and_returns_an_empty_200()
    {
        var (controller, service) = CreateController("{\"Data\":{},\"Success\":true}");

        var result = await controller.DeleteTodoItem(3);

        Assert.True(service.CapturedExecuteIfTimeout);
        Assert.IsType<OkResult>(result);
    }

    [Fact]
    public async Task GetTodoItemById_returns_the_typed_item()
    {
        var (controller, service) = CreateController(
            "{\"Success\":true,\"Data\":{\"Id\":4,\"Title\":\"Buy milk\",\"Description\":\"2%\",\"IsCompleted\":false,\"CreatedAt\":\"2026-07-26T10:00:00Z\",\"IsDeleted\":false}}");

        var result = await controller.GetTodoItemById(4);

        Assert.False(service.CapturedExecuteIfTimeout);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Buy milk", Assert.IsType<TodoItemResponse>(ok.Value).Title);
    }

    [Fact]
    public async Task GetTodosByUserId_returns_the_typed_list()
    {
        var (controller, service) = CreateController(
            "{\"Success\":true,\"Data\":[{\"Id\":4,\"Title\":\"Buy milk\",\"Description\":\"2%\",\"IsCompleted\":false,\"CreatedAt\":\"2026-07-26T10:00:00Z\",\"IsDeleted\":false}]}");

        var result = await controller.GetTodosByUserId(5);

        Assert.False(service.CapturedExecuteIfTimeout);
        var ok = Assert.IsType<OkObjectResult>(result.Result);
        Assert.Equal("Buy milk", Assert.Single(Assert.IsType<List<TodoItemResponse>>(ok.Value)).Title);
    }
}
