using Xunit;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Infrastructure;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TodoApp.WorkerService.Data;
using TodoApp.WorkerService.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the database-readiness gate: connection probing backs off between failed attempts
/// and fails loudly — without running migrations or signaling readiness — when the database
/// never becomes reachable.
/// </summary>
public class DbInitializationServiceTests
{
    /// <summary>
    /// Minimal DI graph serving the single mocked TodoDbContext to the service's scope.
    /// </summary>
    private sealed class SingleContextProvider : IServiceProvider, IServiceScopeFactory, IServiceScope
    {
        private readonly TodoDbContext _dbContext;

        public SingleContextProvider(TodoDbContext dbContext) => _dbContext = dbContext;

        public IServiceProvider ServiceProvider => this;

        public object? GetService(Type serviceType) =>
            serviceType == typeof(IServiceScopeFactory) ? this
            : serviceType == typeof(TodoDbContext) ? _dbContext
            : null;

        public IServiceScope CreateScope() => this;

        public void Dispose() { }
    }

    /// <summary>
    /// Records requested backoff delays instead of waiting, so the retry schedule is
    /// assertable without real time passing.
    /// </summary>
    private sealed class TestableService : DbInitializationService
    {
        public List<double> DelaySeconds { get; } = new();

        public TestableService(IServiceProvider serviceProvider, DbInitializationSignal signal)
            : base(serviceProvider, NullLogger<DbInitializationService>.Instance, signal) { }

        protected override Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken)
        {
            DelaySeconds.Add(delay.TotalSeconds);
            return Task.CompletedTask;
        }
    }

    private static (TestableService Service, Mock<DatabaseFacade> Facade, DbInitializationSignal Signal) CreateService()
    {
        var options = new DbContextOptionsBuilder<TodoDbContext>().Options;
        var dbContext = new Mock<TodoDbContext>(options);
        var facade = new Mock<DatabaseFacade>(dbContext.Object);
        dbContext.SetupGet(c => c.Database).Returns(facade.Object);

        var signal = new DbInitializationSignal();
        var service = new TestableService(new SingleContextProvider(dbContext.Object), signal);
        return (service, facade, signal);
    }

    [Fact]
    public async Task Unreachable_database_fails_startup_with_backoff_instead_of_migrating()
    {
        var (service, facade, signal) = CreateService();
        facade.Setup(f => f.CanConnectAsync(It.IsAny<CancellationToken>())).ReturnsAsync(false);

        // The specific exception and message prove the service failed at the connection gate,
        // not further down inside the migration machinery.
        var ex = await Assert.ThrowsAsync<InvalidOperationException>(
            () => service.StartAsync(CancellationToken.None));
        Assert.Contains("unreachable", ex.Message);

        facade.Verify(f => f.CanConnectAsync(It.IsAny<CancellationToken>()), Times.Exactly(5));
        Assert.Equal(new[] { 1.0, 2.0, 4.0, 8.0 }, service.DelaySeconds);
        Assert.False(signal.Initialization.IsCompleted);
    }

    [Fact]
    public async Task Connection_probe_exceptions_back_off_the_same_way_and_the_last_one_propagates()
    {
        var (service, facade, signal) = CreateService();
        facade.Setup(f => f.CanConnectAsync(It.IsAny<CancellationToken>()))
            .ThrowsAsync(new TimeoutException("db down"));

        await Assert.ThrowsAsync<TimeoutException>(() => service.StartAsync(CancellationToken.None));

        facade.Verify(f => f.CanConnectAsync(It.IsAny<CancellationToken>()), Times.Exactly(5));
        Assert.Equal(new[] { 1.0, 2.0, 4.0, 8.0 }, service.DelaySeconds);
        Assert.False(signal.Initialization.IsCompleted);
    }
}
