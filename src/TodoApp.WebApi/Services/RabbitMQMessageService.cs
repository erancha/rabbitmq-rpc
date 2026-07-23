using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WebApi.Configuration;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.WebApi.Services;

/// <summary>
/// Publishes RPC requests to RabbitMQ and correlates worker replies back to the awaiting caller.
/// The message type sent to the worker is inferred from the message class name.
/// </summary>
public interface IRabbitMQMessageService
{
    /// <summary>
    /// Publishes a message to a RabbitMQ queue and waits for a response using the RabbitMQ RPC pattern.
    /// </summary>
    /// <typeparam name="T">Type of the message to publish</typeparam>
    /// <param name="message">The message to publish</param>
    /// <param name="routingKey">The queue name to publish to</param>
    /// <param name="executeIfTimeout">If true, the worker service will execute the request even if client times out.
    /// Set to true for state-changing operations that should complete regardless of timeout.</param>
    /// <returns>The response message</returns>
    Task<string> PublishMessageRpc<T>(T message, string routingKey, bool executeIfTimeout = false);
}

/// <summary>
/// RPC client over RabbitMQ, used by the WebApi to delegate requests to the worker service.
///
/// Core design:
/// - One durable, instance-specific reply queue ("webapi-replies-{instanceId}") declared at
///   startup, giving each WebApi instance isolated reply traffic for debugging and monitoring
/// - Pending requests are tracked by correlation ID; entries are added on publish and removed
///   when the reply arrives or the request times out, so entries for requests the worker will
///   never answer cannot accumulate
/// - A single long-lived consumer on the reply queue dispatches each reply to its pending request
/// - Publishing borrows short-lived channels from the shared channel pool
///
/// OPEN — the reply consumer has no reconnection/recovery logic; it is a critical path and stops
/// receiving replies if its channel or connection drops.
/// </summary>
public class RabbitMQMessageService : IRabbitMQMessageService
{
    private readonly ObjectPool<IModel> _channelPool;
    private readonly IModel _consumerChannel; // Dedicated long-lived channel for RPC consumer
    private readonly ILogger<RabbitMQMessageService> _logger;
    private readonly WebApiConfig _config;
    private readonly string _replyQueueName; // instance-specific reply queue
    private readonly ConcurrentDictionary<string, TaskCompletionSource<string>> _pendingRequests; // tracks in-flight RPC requests by correlationId

    public RabbitMQMessageService(
        ObjectPool<IModel> channelPool,
        ILogger<RabbitMQMessageService> logger,
        IOptions<WebApiConfig> config
    )
    {
        _channelPool = channelPool;
        _logger = logger;
        _config = config.Value;
        _pendingRequests = new ConcurrentDictionary<string, TaskCompletionSource<string>>();

        // Taken from the pool but never returned — the consumer needs it for the service lifetime.
        _consumerChannel = _channelPool.Get();

        var instanceId = Environment.MachineName;
        _replyQueueName = $"webapi-replies-{instanceId}";

        _consumerChannel.QueueDeclare(queue: _replyQueueName, durable: true);

        var consumer = new EventingBasicConsumer(_consumerChannel);

        consumer.Received += (model, ea) =>
        {
            var correlationId = ea.BasicProperties.CorrelationId;
            var response = Encoding.UTF8.GetString(ea.Body.ToArray());

            _logger.LogInformation(
                "Received RPC response with correlation ID {CorrelationId}",
                correlationId
            );

            // Completing the TaskCompletionSource unblocks the awaiting PublishMessageRpc call
            if (_pendingRequests.TryRemove(correlationId, out var tcs))
                tcs.SetResult(response);
            else
            {
                _logger.LogWarning(
                    "Received response for unknown correlation ID {CorrelationId}",
                    correlationId
                );
            }
        };

        _consumerChannel.BasicConsume(consumer: consumer, queue: _replyQueueName, autoAck: true);
    }

    public async Task<string> PublishMessageRpc<T>(T message, string routingKey, bool executeIfTimeout = false)
    {
        var correlationId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<string>();

        _pendingRequests.TryAdd(correlationId, tcs); // Will always succeed as correlationId is a new GUID

        var publishChannel = _channelPool.Get();
        try
        {
            var properties = publishChannel.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.ReplyTo = _replyQueueName;
            properties.Type = typeof(T).Name;
            // Durable queues alone do not preserve non-persistent messages across a broker restart.
            properties.Persistent = true;

            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            properties.Headers = new Dictionary<string, object>
            {
                { RpcHeaders.TimeoutSeconds, _config.RpcTimeoutSeconds },
                { RpcHeaders.ExecuteIfTimeout, executeIfTimeout }
            };

            var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

            _logger.LogInformation(
                "Publishing RPC request with correlation ID {CorrelationId}",
                correlationId
            );

            publishChannel.BasicPublish(
                exchange: RabbitMQShared.Config.AppExchangeName,
                routingKey: routingKey,
                basicProperties: properties,
                body: body
            );
        }
        finally
        {
            _channelPool.Return(publishChannel);
        }

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_config.RpcTimeoutSeconds));
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
            // Drop the pending entry: the worker never replies to timed-out requests, so a
            // leftover entry would stay in the dictionary forever.
            _pendingRequests.TryRemove(correlationId, out _);

            _logger.LogWarning(
                "Request with correlation ID {CorrelationId} timed out after {TimeoutSeconds} seconds",
                correlationId,
                _config.RpcTimeoutSeconds
            );

            return JsonSerializer.Serialize(
                new RpcResponse
                {
                    Success = false,
                    Error = new RpcError
                    {
                        Kind = RpcErrorKind.TEMPORARY_UNAVAILABLE,
                        // Promises only what the broker guarantees: retention (durable queue,
                        // persistent message, dead-letter routing on failure) — not eventual
                        // processing.
                        Message = $"Service is temporarily unavailable (timeout: {_config.RpcTimeoutSeconds}s)." + (executeIfTimeout ? " Your request remains queued and will not be lost." : string.Empty),
                    },
                }
            );
        }

        return await tcs.Task;
    }
}
