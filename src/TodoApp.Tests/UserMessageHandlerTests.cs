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
/// Verifies UserMessageHandler query behavior against an in-memory database.
/// </summary>
public class UserMessageHandlerTests
{
    /// <summary>
    /// Exposes the protected ProcessMessage entry point so tests can drive the handler with a
    /// test-owned TodoDbContext, bypassing the RabbitMQ consumer pipeline.
    /// </summary>
    private sealed class ProcessInvoker : UserMessageHandler
    {
        public ProcessInvoker(ObjectPool<IModel> channelPool)
            : base(
                channelPool,
                Mock.Of<IServiceScopeFactory>(),
                NullLogger<UserMessageHandler>.Instance,
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
    public async Task GetAllUsers_returns_every_user_not_a_truncated_page()
    {
        using var dbContext = CreateDbContext();
        const int userCount = 150;
        for (var i = 1; i <= userCount; i++)
        {
            dbContext.Users.Add(new User
            {
                Username = $"user{i}",
                Email = $"user{i}@example.com",
                CreatedAt = DateTime.UtcNow,
            });
        }
        await dbContext.SaveChangesAsync();

        var responseJson = await CreateHandler()
            .Invoke(dbContext, nameof(GetAllUsersMessage), "{}");

        var response = JsonSerializer.Deserialize<RpcResponse<GetAllUsersResponse>>(responseJson)!;
        Assert.True(response.Success);
        Assert.Equal(userCount, response.Data!.Users!.Count);
    }
}
