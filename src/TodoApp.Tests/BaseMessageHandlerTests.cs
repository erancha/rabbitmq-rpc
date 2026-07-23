using Xunit;
using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Messages;
using TodoApp.WorkerService.Data;
using TodoApp.WorkerService.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the worker-side message pipeline: exception-to-error-kind mapping in RPC error
/// responses, the timed-out-request skip branch, ack/nack plus reply publication behavior, and
/// the per-message DI scope lifecycle.
/// </summary>
public class BaseMessageHandlerTests
{
    /// <summary>
    /// Test replacement for the DI scope factory. Each CreateScope call returns a scope that
    /// serves a new TodoDbContext (not connected to any database) and counts how many scopes
    /// were created and disposed — letting tests assert that every message was processed in its
    /// own scope and that the scope was disposed when processing finished.
    /// </summary>
    private sealed class TrackingScopeFactory : IServiceScopeFactory
    {
        public int Created { get; private set; }
        public int Disposed { get; private set; }

        public IServiceScope CreateScope()
        {
            Created++;
            return new TrackingScope(this);
        }

        private sealed class TrackingScope : IServiceScope, IServiceProvider
        {
            private readonly TrackingScopeFactory _owner;

            public TrackingScope(TrackingScopeFactory owner) => _owner = owner;

            public IServiceProvider ServiceProvider => this;

            public object? GetService(Type serviceType) =>
                serviceType == typeof(TodoDbContext)
                    ? new TodoDbContext(new DbContextOptionsBuilder<TodoDbContext>().Options)
                    : null;

            public void Dispose() => _owner.Disposed++;
        }
    }

    /// <summary>
    /// Minimal concrete handler exposing the protected error-response factory and delegating
    /// message processing to a test-supplied callback.
    /// </summary>
    private sealed class TestableHandler : BaseMessageHandler
    {
        public List<(string MessageType, string Message)> Processed { get; } = new();
        public Func<string, string, Task<string>> OnProcess { get; set; } =
            (_, _) => Task.FromResult("{\"Success\":true}");

        public TrackingScopeFactory Scopes { get; }

        public TestableHandler(IModel channel, DbInitializationSignal signal)
            : this(channel, signal, new TrackingScopeFactory())
        { }

        private TestableHandler(IModel channel, DbInitializationSignal signal, TrackingScopeFactory scopes)
            : base("test-queue", channel, scopes, NullLogger.Instance, signal)
        {
            Scopes = scopes;
        }

        protected override Task<string> ProcessMessage(TodoDbContext dbContext, string messageType, string message)
        {
            Processed.Add((messageType, message));
            return OnProcess(messageType, message);
        }

        public string InvokeCreateErrorResponse(Exception ex) => CreateErrorResponse(ex);
    }

    private static RpcError ErrorOf(string responseJson)
    {
        var response = JsonSerializer.Deserialize<RpcResponse>(responseJson)!;
        Assert.False(response.Success);
        return response.Error!;
    }

    private static TestableHandler CreateHandler(Mock<IModel> channel)
    {
        var signal = new DbInitializationSignal();
        signal.MarkAsComplete();
        return new TestableHandler(channel.Object, signal);
    }

    #region Error-kind mapping

    [Fact]
    public void NotFoundException_maps_to_NOT_FOUND_with_its_authored_message()
    {
        var handler = CreateHandler(new Mock<IModel>());
        var error = ErrorOf(handler.InvokeCreateErrorResponse(new NotFoundException("user 7 missing")));
        Assert.Equal(RpcErrorKind.NOT_FOUND, error.Kind);
        Assert.Equal("user 7 missing", error.Message);
    }

    [Fact]
    public void ValidationException_maps_to_VALIDATION_with_its_authored_message()
    {
        var handler = CreateHandler(new Mock<IModel>());
        var error = ErrorOf(handler.InvokeCreateErrorResponse(new ValidationException("bad input")));
        Assert.Equal(RpcErrorKind.VALIDATION, error.Kind);
        Assert.Equal("bad input", error.Message);
    }

    [Fact]
    public void Framework_InvalidOperationException_maps_to_FATAL_without_its_message()
    {
        var handler = CreateHandler(new Mock<IModel>());
        var error = ErrorOf(handler.InvokeCreateErrorResponse(
            new InvalidOperationException("No service for type 'TodoDbContext' has been registered.")));
        Assert.Equal(RpcErrorKind.FATAL, error.Kind);
        Assert.Equal(GenericFatalMessage, error.Message);
    }

    [Fact]
    public void Framework_KeyNotFoundException_maps_to_FATAL_without_its_message()
    {
        var handler = CreateHandler(new Mock<IModel>());
        var error = ErrorOf(handler.InvokeCreateErrorResponse(
            new KeyNotFoundException("The given key was not present in the dictionary.")));
        Assert.Equal(RpcErrorKind.FATAL, error.Kind);
        Assert.Equal(GenericFatalMessage, error.Message);
    }

    private const string GenericFatalMessage = "An unexpected error occurred while processing the request.";

    [Theory]
    [InlineData("23505", "A record with this unique value already exists.")] // unique constraint violation
    [InlineData("23503", "The referenced record does not exist.")] // foreign key violation
    public void Postgres_constraint_violation_maps_to_VALIDATION_with_sanitized_message(
        string sqlState, string expectedMessage)
    {
        var handler = CreateHandler(new Mock<IModel>());
        var pgException = new PostgresException(
            "duplicate key value violates unique constraint \"IX_Users_Username\"", "ERROR", "ERROR", sqlState);
        var error = ErrorOf(handler.InvokeCreateErrorResponse(new DbUpdateException("wrapper", pgException)));
        Assert.Equal(RpcErrorKind.VALIDATION, error.Kind);
        Assert.Equal(expectedMessage, error.Message);
    }

    [Fact]
    public void Postgres_error_with_other_sql_state_maps_to_FATAL_without_sql_details()
    {
        var handler = CreateHandler(new Mock<IModel>());
        var pgException = new PostgresException("relation \"Users\" does not exist", "ERROR", "ERROR", "42P01");
        var error = ErrorOf(handler.InvokeCreateErrorResponse(new DbUpdateException("wrapper", pgException)));
        Assert.Equal(RpcErrorKind.FATAL, error.Kind);
        Assert.Equal(GenericFatalMessage, error.Message);
    }

    [Fact]
    public void Unrecognized_exception_maps_to_FATAL_without_exception_details()
    {
        var handler = CreateHandler(new Mock<IModel>());
        var error = ErrorOf(handler.InvokeCreateErrorResponse(new ArgumentException("boom")));
        Assert.Equal(RpcErrorKind.FATAL, error.Kind);
        Assert.Equal(GenericFatalMessage, error.Message);
    }

    #endregion

    #region Consumer pipeline

    /// <summary>
    /// Starts a handler against a mocked channel and returns the captured consumer plus the
    /// list of replies the handler publishes.
    /// </summary>
    private sealed class ConsumerHarness
    {
        public Mock<IModel> Channel { get; } = new();
        public TestableHandler Handler { get; }
        public EventingBasicConsumer Consumer { get; private set; } = null!;
        public List<(string Exchange, string RoutingKey, IBasicProperties Props, byte[] Body)> Published { get; } = new();

        // Channel operations ("ack" / "nack" / "publish") in invocation order, for settlement-order assertions.
        public List<string> Calls { get; } = new();

        public ConsumerHarness()
        {
            Channel
                .Setup(c => c.BasicAck(It.IsAny<ulong>(), It.IsAny<bool>()))
                .Callback(() => Calls.Add("ack"));

            Channel
                .Setup(c => c.BasicNack(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>()))
                .Callback(() => Calls.Add("nack"));

            Channel
                .Setup(c => c.BasicConsume(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
                .Callback((string queue, bool autoAck, string tag, bool noLocal, bool exclusive,
                    IDictionary<string, object> args, IBasicConsumer consumer) =>
                    Consumer = (EventingBasicConsumer)consumer)
                .Returns("consumer-tag");

            Channel.Setup(c => c.CreateBasicProperties()).Returns(() =>
            {
                var props = new Mock<IBasicProperties>();
                props.SetupAllProperties();
                return props.Object;
            });

            Channel
                .Setup(c => c.BasicPublish(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<IBasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>()))
                .Callback((string exchange, string routingKey, bool mandatory,
                    IBasicProperties props, ReadOnlyMemory<byte> body) =>
                {
                    Calls.Add("publish");
                    Published.Add((exchange, routingKey, props, body.ToArray()));
                });

            Handler = CreateHandler(Channel);
        }

        public static async Task<ConsumerHarness> CreateStartedAsync()
        {
            var harness = new ConsumerHarness();
            await harness.Handler.StartAsync(CancellationToken.None);
            return harness;
        }

        public void Deliver(long ageSeconds, int timeoutSeconds, bool? executeIfTimeout, ulong deliveryTag = 7)
        {
            var props = new Mock<IBasicProperties>();
            props.SetupAllProperties();
            props.Object.Type = "PingMessage";
            props.Object.CorrelationId = "corr-1";
            props.Object.ReplyTo = "reply-queue";
            props.Object.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds() - ageSeconds);
            var headers = new Dictionary<string, object> { { RpcHeaders.TimeoutSeconds, timeoutSeconds } };
            if (executeIfTimeout.HasValue)
                headers.Add(RpcHeaders.ExecuteIfTimeout, executeIfTimeout.Value);
            props.Object.Headers = headers;

            Consumer.HandleBasicDeliver(
                "consumer-tag", deliveryTag, false, "", "test-queue",
                props.Object, Encoding.UTF8.GetBytes("{\"Name\":\"a\"}"));
        }
    }

    [Fact]
    public async Task Timed_out_request_is_acked_and_skipped()
    {
        var harness = await ConsumerHarness.CreateStartedAsync();

        harness.Deliver(ageSeconds: 100, timeoutSeconds: 5, executeIfTimeout: false);

        Assert.Empty(harness.Handler.Processed);
        Assert.Empty(harness.Published);
        harness.Channel.Verify(c => c.BasicAck(7, false), Times.Once);
    }

    [Fact]
    public async Task Timed_out_request_with_executeIfTimeout_is_processed_but_reply_suppressed()
    {
        var harness = await ConsumerHarness.CreateStartedAsync();

        harness.Deliver(ageSeconds: 100, timeoutSeconds: 5, executeIfTimeout: true);

        Assert.Single(harness.Handler.Processed);
        Assert.Empty(harness.Published);
        harness.Channel.Verify(c => c.BasicAck(7, false), Times.Once);
    }

    [Fact]
    public async Task Fresh_request_is_processed_and_reply_routed_to_reply_queue_with_correlation_id()
    {
        var harness = await ConsumerHarness.CreateStartedAsync();
        harness.Handler.OnProcess = (_, _) => Task.FromResult("{\"Success\":true}");

        harness.Deliver(ageSeconds: 0, timeoutSeconds: 30, executeIfTimeout: false);

        Assert.Single(harness.Handler.Processed);
        Assert.Equal("PingMessage", harness.Handler.Processed[0].MessageType);
        harness.Channel.Verify(c => c.BasicAck(7, false), Times.Once);

        var (exchange, routingKey, props, body) = Assert.Single(harness.Published);
        Assert.Equal("", exchange);
        Assert.Equal("reply-queue", routingKey);
        Assert.Equal("corr-1", props.CorrelationId);
        Assert.Equal("{\"Success\":true}", Encoding.UTF8.GetString(body));
    }

    [Fact]
    public async Task Failing_request_is_nacked_before_the_error_reply_is_published()
    {
        var harness = await ConsumerHarness.CreateStartedAsync();
        harness.Handler.OnProcess = (_, _) => throw new NotFoundException("no such user");

        harness.Deliver(ageSeconds: 0, timeoutSeconds: 30, executeIfTimeout: false);

        harness.Channel.Verify(c => c.BasicNack(7, false, false), Times.Once);
        var (_, routingKey, _, body) = Assert.Single(harness.Published);
        Assert.Equal("reply-queue", routingKey);
        var error = ErrorOf(Encoding.UTF8.GetString(body));
        Assert.Equal(RpcErrorKind.NOT_FOUND, error.Kind);
        Assert.Equal("no such user", error.Message);

        // Nack must precede the reply publish: if the reply publish fails, the message is
        // already dead-lettered rather than left unsettled.
        Assert.Equal(new[] { "nack", "publish" }, harness.Calls);
    }

    [Fact]
    public async Task Reply_failure_after_successful_processing_does_not_nack_the_acked_delivery()
    {
        var harness = await ConsumerHarness.CreateStartedAsync();
        // Only the success-reply publish fails; a later error-reply publish would succeed and
        // must still not lead to a nack of the already-acked delivery.
        var publishAttempts = 0;
        harness.Channel
            .Setup(c => c.BasicPublish(
                It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                It.IsAny<IBasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>()))
            .Callback(() =>
            {
                if (++publishAttempts == 1)
                    throw new InvalidOperationException("publish failed");
            });

        harness.Deliver(ageSeconds: 0, timeoutSeconds: 30, executeIfTimeout: false);

        Assert.Single(harness.Handler.Processed);
        harness.Channel.Verify(c => c.BasicAck(7, false), Times.Once);
        // Nacking an already-acked delivery tag is a channel-level protocol error that would
        // close the channel; the delivery is settled and the client simply times out.
        harness.Channel.Verify(
            c => c.BasicNack(It.IsAny<ulong>(), It.IsAny<bool>(), It.IsAny<bool>()), Times.Never);
    }

    [Fact]
    public async Task Each_delivery_is_processed_in_its_own_scope_that_is_disposed_afterwards()
    {
        var harness = await ConsumerHarness.CreateStartedAsync();

        harness.Deliver(ageSeconds: 0, timeoutSeconds: 30, executeIfTimeout: false);

        Assert.Equal(1, harness.Handler.Scopes.Created);
        Assert.Equal(1, harness.Handler.Scopes.Disposed);
    }

    [Fact]
    public async Task Scope_is_disposed_even_when_processing_fails()
    {
        var harness = await ConsumerHarness.CreateStartedAsync();
        harness.Handler.OnProcess = (_, _) => throw new InvalidOperationException("boom");

        harness.Deliver(ageSeconds: 0, timeoutSeconds: 30, executeIfTimeout: false);

        Assert.Equal(1, harness.Handler.Scopes.Created);
        Assert.Equal(1, harness.Handler.Scopes.Disposed);
    }

    #endregion
}
