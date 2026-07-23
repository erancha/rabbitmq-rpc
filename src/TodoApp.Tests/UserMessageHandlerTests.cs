using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Moq;
using RabbitMQ.Client;
using TodoApp.WorkerService.Data;
using TodoApp.WorkerService.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the per-message DI scope lifecycle in UserMessageHandler: each processed message
/// resolves its DbContext from a fresh scope that must be disposed when processing completes.
/// </summary>
public class UserMessageHandlerTests
{
    /// <summary>
    /// Exposes the protected message-processing entry point for direct invocation.
    /// </summary>
    private sealed class ExposedUserMessageHandler : UserMessageHandler
    {
        public ExposedUserMessageHandler(
            ObjectPool<IModel> channelPool,
            IServiceScopeFactory scopeFactory,
            DbInitializationSignal signal)
            : base(channelPool, scopeFactory, NullLogger<UserMessageHandler>.Instance, signal)
        { }

        public Task<string> InvokeProcessMessage(string messageType, string message) =>
            ProcessMessage(messageType, message);
    }

    [Fact]
    public async Task Per_message_scope_is_disposed_when_processing_completes()
    {
        var scopeDisposed = false;
        var dbContext = new TodoDbContext(new DbContextOptionsBuilder<TodoDbContext>().Options);

        var provider = new Mock<IServiceProvider>();
        provider.Setup(p => p.GetService(typeof(TodoDbContext))).Returns(dbContext);

        var scope = new Mock<IServiceScope>();
        scope.SetupGet(s => s.ServiceProvider).Returns(provider.Object);
        scope.Setup(s => s.Dispose()).Callback(() => scopeDisposed = true);

        var scopeFactory = new Mock<IServiceScopeFactory>();
        scopeFactory.Setup(f => f.CreateScope()).Returns(scope.Object);

        var channelPool = new Mock<ObjectPool<IModel>>();
        channelPool.Setup(p => p.Get()).Returns(new Mock<IModel>().Object);

        var signal = new DbInitializationSignal();
        signal.MarkAsComplete();

        var handler = new ExposedUserMessageHandler(channelPool.Object, scopeFactory.Object, signal);

        // The unknown-message-type path resolves the DbContext from the scope and then throws,
        // without touching the database; the scope must be disposed on this exit path too.
        await Assert.ThrowsAsync<InvalidOperationException>(
            () => handler.InvokeProcessMessage("UnknownMessage", "{}"));

        Assert.True(scopeDisposed, "The DI scope created for the message was not disposed.");
    }
}
