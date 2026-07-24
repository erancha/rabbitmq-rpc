using Xunit;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Moq;
using RabbitMQ.Client;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WorkerService.Data;
using TodoApp.WorkerService.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies TodoItemMessageHandler query behavior against an in-memory database.
/// </summary>
public class TodoItemMessageHandlerTests
{
    /// <summary>
    /// Exposes the protected ProcessMessage entry point so tests can drive the handler with a
    /// test-owned TodoDbContext, bypassing the RabbitMQ consumer pipeline.
    /// </summary>
    private sealed class ProcessInvoker : TodoItemMessageHandler
    {
        public ProcessInvoker(ObjectPool<IModel> channelPool)
            : base(
                channelPool,
                Mock.Of<IServiceScopeFactory>(),
                NullLogger<TodoItemMessageHandler>.Instance,
                new DbInitializationSignal())
        { }

        public Task<string> Invoke(TodoDbContext dbContext, string messageType, string message) =>
            ProcessMessage(dbContext, messageType, message);
    }

    private static ProcessInvoker CreateHandler()
    {
        var pool = new Mock<ObjectPool<IModel>>();
        pool.Setup(p => p.Get()).Returns(Mock.Of<IModel>());
        return new ProcessInvoker(pool.Object);
    }

    private static TodoDbContext CreateDbContext() =>
        new(new DbContextOptionsBuilder<TodoDbContext>()
            .UseInMemoryDatabase(Guid.NewGuid().ToString())
            .Options);

    [Fact]
    public async Task GetTodoItemById_returns_the_item_data()
    {
        using var dbContext = CreateDbContext();
        var todoItem = new TodoItem
        {
            Title = "write tests",
            Description = "for the single-item query",
            UserId = 1,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.TodoItems.Add(todoItem);
        await dbContext.SaveChangesAsync();

        var responseJson = await CreateHandler().Invoke(
            dbContext,
            nameof(GetTodoItemByIdMessage),
            $"{{\"Id\":{todoItem.Id}}}");

        var response = JsonSerializer.Deserialize<RpcResponse<TodoItemResponse>>(responseJson)!;
        Assert.True(response.Success);
        Assert.Equal(todoItem.Id, response.Data!.Id);
        Assert.Equal("write tests", response.Data.Title);
    }

    [Fact]
    public async Task GetTodoItemById_soft_deleted_item_reports_not_found()
    {
        using var dbContext = CreateDbContext();
        var todoItem = new TodoItem
        {
            Title = "gone",
            UserId = 1,
            CreatedAt = DateTime.UtcNow,
            IsDeleted = true,
            DeletedAt = DateTime.UtcNow,
        };
        dbContext.TodoItems.Add(todoItem);
        await dbContext.SaveChangesAsync();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateHandler().Invoke(
                dbContext,
                nameof(GetTodoItemByIdMessage),
                $"{{\"Id\":{todoItem.Id}}}"));
    }
}
