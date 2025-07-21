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
/// Provides a centralized service for publishing messages to RabbitMQ queues.
/// This service eliminates code duplication across controllers and standardizes message publishing.
/// Key benefits:
/// - Automatic message type inference from class names
/// - Single responsibility for RabbitMQ publishing logic
/// - Consistent message serialization across the application
/// - Easier testing through dependency injection
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
/// Default implementation of IRabbitMQMessageService that handles message publishing to RabbitMQ.
/// Design changes needed for single reply queue approach:
/// 1. Create an instance-specific reply queue at service startup instead of per request
///    - Queue should be durable and named with instance ID (e.g. "webapi-replies-{instanceId}")
///    - Better debugging, monitoring and isolation compared to shared queue approach (in which all WebApi instances share one reply queue)
/// 2. Use correlationId as key in ConcurrentDictionary<string, TaskCompletionSource<string>>
///    - Add pending requests on publish, remove when response received
///    - Clean up expired requests periodically to prevent memory leaks
/// 3. Single consumer on reply queue dispatches to correct request using correlationId
///    - More efficient than creating/destroying consumers per request
///    - Requires thread-safe response routing via ConcurrentDictionary
/// 4. Consider implementing consumer reconnection/recovery logic
///    - Single queue/consumer is a critical path, needs robust error handling
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

        // Get a dedicated long-lived channel from the pool for the RPC consumer
        // We don't return this one to the pool - it stays with the service
        _consumerChannel = _channelPool.Get();

        // Create a durable, named, instance-specific reply queue
        var instanceId = Environment.MachineName; // Or use another unique identifier
        _replyQueueName = $"webapi-replies-{instanceId}";

        // QueueDeclare options:
        // - durable: queue and messages survive broker restarts (default=false)
        // - autoDelete: delete queue when last consumer disconnects (default=false)
        // - exclusive: only allow access from the declaring connection (default: true)
        // - arguments: optional settings like TTL, max length (default=null)
        _consumerChannel.QueueDeclare(queue: _replyQueueName, durable: true);

        // Set up consumer for replies: Create a consumer, add an event handler, and register the consumer.
        var consumer = new EventingBasicConsumer(_consumerChannel);

        consumer.Received += (model, ea) =>
        {
            var correlationId = ea.BasicProperties.CorrelationId;
            var response = Encoding.UTF8.GetString(ea.Body.ToArray());

            _logger.LogInformation(
                "Received RPC response with correlation ID {CorrelationId}",
                correlationId
            );

            // If found in _pendingRequests, complete the task with the response value
            // This unblocks the waiting PublishMessageRpc call
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

    /// <summary>
    /// Publishes a message to a RabbitMQ queue and waits for a response using the RPC pattern.
    /// </summary>
    /// <param name="message">The message to publish</param>
    /// <param name="routingKey">The queue name to publish to</param>
    /// <param name="executeIfTimeout">If true, the workers service will execute the request even if client times out. 
    /// Set to true for state-changing operations that should complete regardless of timeout.</param>
    /// <returns>The response message</returns>
    public async Task<string> PublishMessageRpc<T>(T message, string routingKey, bool executeIfTimeout = false)
    {
        var correlationId = Guid.NewGuid().ToString();
        var tcs = new TaskCompletionSource<string>();

        // Store the TaskCompletionSource for this request
        _pendingRequests.TryAdd(correlationId, tcs); // Will always succeed as correlationId is a new GUID

        // Get a channel from the pool for publishing
        var publishChannel = _channelPool.Get();
        try
        {
            var properties = publishChannel.CreateBasicProperties();
            properties.CorrelationId = correlationId;
            properties.ReplyTo = _replyQueueName;
            properties.Type = typeof(T).Name;

            properties.Timestamp = new AmqpTimestamp(DateTimeOffset.UtcNow.ToUnixTimeSeconds());
            properties.Headers = new Dictionary<string, object>
            {
                { "timeout_seconds", _config.RpcTimeoutSeconds },
                { "execute_if_timeout", executeIfTimeout }
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
            // Always return the channel to the pool
            _channelPool.Return(publishChannel);
        }

        // Create timeout task that completes after specified seconds
        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_config.RpcTimeoutSeconds));

        // Block until either:
        // 1. Response received (tcs completed by consumer)
        // 2. Timeout occurs
        var completedTask = await Task.WhenAny(tcs.Task, timeoutTask);

        if (completedTask == timeoutTask)
        {
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
                        Kind = "TEMPORARY_UNAVAILABLE",
                        Message = $"Service is temporarily unavailable (timeout: {_config.RpcTimeoutSeconds}s)." + (executeIfTimeout ? " Your request is queued and will be processed when the system recovers" : string.Empty),
                    },
                }
            );
        }

        return await tcs.Task;
    }
}
