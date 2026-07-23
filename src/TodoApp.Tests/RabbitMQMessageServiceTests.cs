using Xunit;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using Moq;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Messages;
using TodoApp.WebApi.Configuration;
using TodoApp.WebApi.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the WebApi RPC client: correlation-ID routing of replies to the matching pending
/// request, the timeout fallback response, and the AMQP metadata attached to published requests.
/// </summary>
public class RabbitMQMessageServiceTests
{
    private sealed record PingMessage(string Name);

    /// <summary>
    /// Records every log entry as "Level:message" so tests can assert on the service's
    /// logging contract, the only externally observable signal for reply routing decisions.
    /// </summary>
    private sealed class CapturingLogger : ILogger<RabbitMQMessageService>
    {
        public List<string> Messages { get; } = new();

        public IDisposable? BeginScope<TState>(TState state) where TState : notnull => null;

        public bool IsEnabled(LogLevel logLevel) => true;

        public void Log<TState>(LogLevel logLevel, EventId eventId, TState state,
            Exception? exception, Func<TState, Exception?, string> formatter)
            => Messages.Add($"{logLevel}:{formatter(state, exception)}");
    }

    /// <summary>
    /// Builds a RabbitMQMessageService on top of a mocked channel pool, capturing the reply
    /// consumer the service registers and every message it publishes.
    /// </summary>
    private sealed class Harness
    {
        public RabbitMQMessageService Service { get; }
        public EventingBasicConsumer ReplyConsumer { get; private set; } = null!;
        public List<(string Exchange, string RoutingKey, IBasicProperties Props, byte[] Body)> Published { get; } = new();

        public Harness(int rpcTimeoutSeconds, ILogger<RabbitMQMessageService>? logger = null)
        {
            var channel = new Mock<IModel>();

            channel.Setup(c => c.CreateBasicProperties()).Returns(() =>
            {
                var props = new Mock<IBasicProperties>();
                props.SetupAllProperties();
                return props.Object;
            });

            channel
                .Setup(c => c.BasicConsume(
                    It.IsAny<string>(), It.IsAny<bool>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<bool>(), It.IsAny<IDictionary<string, object>>(), It.IsAny<IBasicConsumer>()))
                .Callback((string queue, bool autoAck, string tag, bool noLocal, bool exclusive,
                    IDictionary<string, object> args, IBasicConsumer consumer) =>
                    ReplyConsumer = (EventingBasicConsumer)consumer)
                .Returns("consumer-tag");

            channel
                .Setup(c => c.BasicPublish(
                    It.IsAny<string>(), It.IsAny<string>(), It.IsAny<bool>(),
                    It.IsAny<IBasicProperties>(), It.IsAny<ReadOnlyMemory<byte>>()))
                .Callback((string exchange, string routingKey, bool mandatory,
                    IBasicProperties props, ReadOnlyMemory<byte> body) =>
                    Published.Add((exchange, routingKey, props, body.ToArray())));

            var pool = new Mock<ObjectPool<IModel>>();
            pool.Setup(p => p.Get()).Returns(channel.Object);

            Service = new RabbitMQMessageService(
                pool.Object,
                logger ?? NullLogger<RabbitMQMessageService>.Instance,
                Options.Create(new WebApiConfig { RpcTimeoutSeconds = rpcTimeoutSeconds }));
        }

        public void DeliverReply(string correlationId, string payload)
        {
            var props = new Mock<IBasicProperties>();
            props.SetupAllProperties();
            props.Object.CorrelationId = correlationId;
            ReplyConsumer.HandleBasicDeliver(
                "consumer-tag", 1, false, "", "", props.Object, Encoding.UTF8.GetBytes(payload));
        }
    }

    [Fact]
    public async Task Reply_with_matching_correlation_id_completes_the_request()
    {
        var harness = new Harness(rpcTimeoutSeconds: 30);

        var task = harness.Service.PublishMessageRpc(new PingMessage("a"), "some-queue");
        var correlationId = harness.Published.Single().Props.CorrelationId;

        harness.DeliverReply(correlationId, "{\"Success\":true}");

        Assert.Equal("{\"Success\":true}", await task);
    }

    [Fact]
    public async Task Reply_with_unknown_correlation_id_is_ignored()
    {
        var harness = new Harness(rpcTimeoutSeconds: 30);

        var task = harness.Service.PublishMessageRpc(new PingMessage("a"), "some-queue");
        var correlationId = harness.Published.Single().Props.CorrelationId;

        harness.DeliverReply("unknown-correlation-id", "{\"Success\":false}");
        Assert.False(task.IsCompleted);

        harness.DeliverReply(correlationId, "{\"Success\":true}");
        Assert.Equal("{\"Success\":true}", await task);
    }

    [Fact]
    public async Task Concurrent_requests_each_receive_their_own_reply()
    {
        var harness = new Harness(rpcTimeoutSeconds: 30);

        var first = harness.Service.PublishMessageRpc(new PingMessage("first"), "some-queue");
        var second = harness.Service.PublishMessageRpc(new PingMessage("second"), "some-queue");
        var firstId = harness.Published[0].Props.CorrelationId;
        var secondId = harness.Published[1].Props.CorrelationId;

        // Replies arrive out of order; routing must go by correlation ID, not arrival order.
        harness.DeliverReply(secondId, "\"reply-two\"");
        harness.DeliverReply(firstId, "\"reply-one\"");

        Assert.Equal("\"reply-one\"", await first);
        Assert.Equal("\"reply-two\"", await second);
    }

    [Fact]
    public async Task Timeout_returns_temporary_unavailable_error()
    {
        var harness = new Harness(rpcTimeoutSeconds: 1);

        var responseJson = await harness.Service.PublishMessageRpc(new PingMessage("a"), "some-queue");

        var response = JsonSerializer.Deserialize<RpcResponse>(responseJson);
        Assert.NotNull(response);
        Assert.False(response!.Success);
        Assert.Equal(RpcErrorKind.TEMPORARY_UNAVAILABLE, response.Error!.Kind);
        Assert.DoesNotContain("queued", response.Error.Message);
    }

    [Fact]
    public async Task Timeout_with_executeIfTimeout_reports_request_still_queued()
    {
        var harness = new Harness(rpcTimeoutSeconds: 1);

        var responseJson = await harness.Service.PublishMessageRpc(
            new PingMessage("a"), "some-queue", executeIfTimeout: true);

        var response = JsonSerializer.Deserialize<RpcResponse>(responseJson);
        Assert.False(response!.Success);
        Assert.Equal(RpcErrorKind.TEMPORARY_UNAVAILABLE, response.Error!.Kind);
        Assert.Contains("queued", response.Error.Message);
        // The system guarantees retention (durable queue, persistent message, dead-letter
        // routing on failure) — not eventual processing, so the message must not promise it.
        Assert.Contains("will not be lost", response.Error.Message);
        Assert.DoesNotContain("will be processed", response.Error.Message);
    }

    [Fact]
    public async Task Timed_out_request_is_removed_so_a_late_reply_is_treated_as_unknown()
    {
        var logger = new CapturingLogger();
        var harness = new Harness(rpcTimeoutSeconds: 1, logger);

        await harness.Service.PublishMessageRpc(new PingMessage("a"), "some-queue");
        var correlationId = harness.Published.Single().Props.CorrelationId;

        // A leaked pending-request entry would route this late reply to the timed-out
        // request's TaskCompletionSource instead of the unknown-correlation-ID path.
        harness.DeliverReply(correlationId, "{\"Success\":true}");

        Assert.Contains(logger.Messages, m =>
            m.StartsWith($"{LogLevel.Warning}:") &&
            m.Contains("unknown correlation ID") &&
            m.Contains(correlationId));
    }

    [Fact]
    public async Task Published_request_carries_reply_queue_type_and_timeout_metadata()
    {
        var harness = new Harness(rpcTimeoutSeconds: 30);

        var task = harness.Service.PublishMessageRpc(
            new PingMessage("a"), "some-queue", executeIfTimeout: true);

        var (exchange, routingKey, props, _) = harness.Published.Single();
        Assert.Equal(RabbitMQShared.Config.AppExchangeName, exchange);
        Assert.Equal("some-queue", routingKey);
        Assert.Equal($"webapi-replies-{Environment.MachineName}", props.ReplyTo);
        Assert.Equal(nameof(PingMessage), props.Type);
        Assert.Equal(30, props.Headers[RpcHeaders.TimeoutSeconds]);
        Assert.True((bool)props.Headers[RpcHeaders.ExecuteIfTimeout]);
        // Durable queues alone do not preserve non-persistent messages across a broker restart.
        Assert.True(props.Persistent);

        harness.DeliverReply(props.CorrelationId, "{}");
        await task;
    }
}
