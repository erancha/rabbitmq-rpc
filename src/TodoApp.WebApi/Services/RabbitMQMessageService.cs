using System.Text;
using System.Text.Json;
using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Configuration;
using TodoApp.Shared.Models;
using TodoApp.WebApi.Configuration;

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
    /// Publishes a message to a RabbitMQ queue with automatic message type inference.
    /// </summary>
    /// <typeparam name="T">Type of the message to publish</typeparam>
    /// <param name="message">The message to publish</param>
    /// <param name="routingKey">The queue name to publish to</param>
    void PublishMessage<T>(T message, string routingKey);

    /// <summary>
    /// Publishes a message to a RabbitMQ queue and waits for a response using the RabbitMQ RPC pattern.
    /// </summary>
    /// <typeparam name="T">Type of the message to publish</typeparam>
    /// <param name="message">The message to publish</param>
    /// <param name="routingKey">The queue name to publish to</param>
    /// <returns>The response message</returns>
    RpcResponse PublishMessageRpc<T>(T message, string routingKey);
}

/// <summary>
/// Default implementation of IRabbitMQMessageService that handles message publishing to RabbitMQ.
/// Uses the message class name to infer the message type.
/// </summary>
public class RabbitMQMessageService : IRabbitMQMessageService
{
    private readonly IModel _channel;
    private readonly ILogger<RabbitMQMessageService> _logger;
    private readonly WebApiConfig _config;

    public RabbitMQMessageService(
        IModel channel,
        ILogger<RabbitMQMessageService> logger,
        IOptions<WebApiConfig> config
    )
    {
        _channel = channel;
        _logger = logger;
        _config = config.Value;
    }

    public void PublishMessage<T>(T message, string routingKey)
    {
        var json = JsonSerializer.Serialize(message);
        var body = Encoding.UTF8.GetBytes(json);

        var properties = _channel.CreateBasicProperties();
        properties.Type = typeof(T).Name;

        _channel.BasicPublish(
            exchange: RabbitMQConfig.AppExchangeName,
            routingKey: routingKey,
            basicProperties: properties,
            body: body
        );
    }

    public RpcResponse PublishMessageRpc<T>(T message, string routingKey)
    {
        var correlationId = Guid.NewGuid().ToString();
        var replyQueueName = _channel.QueueDeclare().QueueName;
        var tcs = new TaskCompletionSource<string>();

        var properties = _channel.CreateBasicProperties();
        properties.CorrelationId = correlationId;
        properties.ReplyTo = replyQueueName;
        properties.Type = typeof(T).Name;

        var body = Encoding.UTF8.GetBytes(JsonSerializer.Serialize(message));

        var consumer = new EventingBasicConsumer(_channel);
        consumer.Received += (model, ea) =>
        {
#if DEBUG
            System.Diagnostics.Debug.Assert(
                ea.BasicProperties.CorrelationId == correlationId,
                "CorrelationId mismatch in RPC response"
            );
#endif
            var response = Encoding.UTF8.GetString(ea.Body.ToArray());
            if (_config.EnableRequestLogging)
            {
                _logger.LogInformation(
                    "Received RPC response for message type {MessageType} with correlation ID {CorrelationId}",
                    typeof(T).Name,
                    correlationId
                );
            }
            tcs.SetResult(response);
        };

        _channel.BasicConsume(consumer: consumer, queue: replyQueueName, autoAck: true);

        if (_config.EnableRequestLogging)
        {
            _logger.LogInformation(
                "Sending RPC request for message type {MessageType} with correlation ID {CorrelationId}",
                typeof(T).Name,
                correlationId
            );
        }

        _channel.BasicPublish(
            exchange: RabbitMQConfig.AppExchangeName,
            routingKey: routingKey,
            basicProperties: properties,
            body: body
        );

        var timeoutTask = Task.Delay(TimeSpan.FromSeconds(_config.RpcTimeoutSeconds));
        var completedTask = Task.WhenAny(tcs.Task, timeoutTask).Result;

        if (completedTask == timeoutTask)
        {
            if (_config.EnableRequestLogging)
            {
                _logger.LogWarning(
                    "RPC request timed out after {TimeoutSeconds} seconds. Request will be processed when worker service recovers",
                    _config.RpcTimeoutSeconds
                );
            }

            return new RpcResponse
            {
                Success = false,
                Error = new RpcError
                {
                    Kind = "TEMPORARY_UNAVAILABLE",
                    Message =
                        $"Service is temporarily unavailable (timeout: {_config.RpcTimeoutSeconds}s). Your request is queued and will be processed when the system recovers",
                },
            };
        }

        var responseJson = tcs.Task.Result;
        return JsonSerializer.Deserialize<RpcResponse>(responseJson)!
            ?? new RpcResponse
            {
                Success = false,
                Error = new RpcError { Kind = "FATAL", Message = "Failed to deserialize response" },
            };
    }
}
