using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Messages;
using TodoApp.WorkerService.Data;
using static TodoApp.Shared.Messages.RpcErrorKind;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// Base class for RabbitMQ message handlers providing common RPC response functionality
/// and message processing infrastructure.
/// </summary>
public abstract class BaseMessageHandler : IHostedService, IDisposable
{
    protected readonly IModel _channel;
    protected readonly IServiceScopeFactory _scopeFactory;
    protected readonly ILogger _logger;
    protected readonly string _queueName;

    protected BaseMessageHandler(
        string queueName,
        IModel channel,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        InitializationSignal initializationSignal
    )
    {
        _queueName = queueName;
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _initializationSignal = initializationSignal;
    }

    private readonly InitializationSignal _initializationSignal;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Waiting for database initialization before starting {_queueName}",
            _queueName
        );
        await _initializationSignal.Initialization;
        _logger.LogInformation("Starting to consume messages from {_queueName}", _queueName);
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false);
        var consumer = new EventingBasicConsumer(_channel);
        _channel.BasicConsume(queue: _queueName, autoAck: false, consumer: consumer);

        consumer.Received += async (model, ea) =>
        {
            if (cancellationToken.IsCancellationRequested)
            {
                return;
            }
            try
            {
                var messageType = ea.BasicProperties?.Type;
                if (string.IsNullOrEmpty(messageType))
                {
                    _logger.LogWarning("Message type is missing");
                    var errorResponse = CreateErrorResponse(
                        new InvalidOperationException("Message type is missing")
                    );
                    SendRpcResponse(ea, errorResponse);
                    return;
                }

                // Check for timeout first to potentially skip processing
                if (HasRequestTimedOut(ea, messageType, out var elapsedSeconds, out var timeoutSeconds))
                {
                    var executeIfTimeout = ea.BasicProperties?.Headers?.TryGetValue("execute_if_timeout", out var executeObj) == true
                        && Convert.ToBoolean(executeObj);

                    if (!executeIfTimeout)
                    {
                        _logger.LogInformation(
                            "Skipping response for timed out request {MessageType} (elapsed: {ElapsedSeconds}s)",
                            messageType, elapsedSeconds
                        );
                        _channel.BasicAck(ea.DeliveryTag, multiple: false);
                        return;
                    }
                }

                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                var rpcResponse = await ProcessMessage(messageType, message);
                _channel.BasicAck(ea.DeliveryTag, multiple: false);

                if (!HasRequestTimedOut(ea, messageType, out _, out _)) 
                    SendRpcResponse(ea, rpcResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message");
                var rpcResponse = CreateErrorResponse(ex);
                SendRpcResponse(ea, rpcResponse);
                _channel.BasicNack(ea.DeliveryTag, false, false);
            }
        };

        _logger.LogInformation("Consumer started for {_queueName}", _queueName);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping consumer for {_queueName}", _queueName);
        return Task.CompletedTask;
    }

    protected abstract Task<string> ProcessMessage(string messageType, string message);

    public virtual void Dispose()
    {
        // No resources to dispose in base class
    }

    #region Request and Response Helpers

    /// <summary>
    /// Checks if a request has exceeded its timeout duration.
    /// </summary>
    /// <param name="ea">The delivery event args containing message properties</param>
    /// <param name="messageType">The type of message being processed</param>
    /// <param name="elapsedSeconds">Output parameter for the elapsed time since request was sent</param>
    /// <param name="timeoutSeconds">Output parameter for the request's timeout duration</param>
    /// <returns>True if the request has timed out, false otherwise</returns>
    private bool HasRequestTimedOut(
        BasicDeliverEventArgs ea,
        string messageType,
        out long elapsedSeconds,
        out int timeoutSeconds
    )
    {
        elapsedSeconds = 0;
        timeoutSeconds = 30; // Default timeout

        if (ea.BasicProperties?.Timestamp == null || ea.BasicProperties?.Headers == null)
            return false;

        var requestTimestamp = ea.BasicProperties.Timestamp.UnixTime;
        timeoutSeconds = ea.BasicProperties.Headers.TryGetValue("timeout_seconds", out var timeoutObj)
            ? Convert.ToInt32(timeoutObj)
            : timeoutSeconds;

        var currentTime = DateTimeOffset.UtcNow.ToUnixTimeSeconds();
        elapsedSeconds = currentTime - requestTimestamp;

        var hasTimedOut = elapsedSeconds > timeoutSeconds;
        if (hasTimedOut)
        {
            _logger.LogWarning(
                "Request {MessageType} timed out after {ElapsedSeconds}s (timeout: {TimeoutSeconds}s)",
                messageType, elapsedSeconds, timeoutSeconds
            );
        }

        return hasTimedOut;
    }

    protected void SendRpcResponse(BasicDeliverEventArgs ea, string rpcResponse)
    {
        if (!string.IsNullOrEmpty(ea.BasicProperties.ReplyTo))
        {
            var replyProps = _channel.CreateBasicProperties();
            replyProps.CorrelationId = ea.BasicProperties.CorrelationId;
            var responseBytes = Encoding.UTF8.GetBytes(rpcResponse);
            _channel.BasicPublish("", ea.BasicProperties.ReplyTo, replyProps, responseBytes);
        }
    }

    protected string CreateSuccessResponse()
    {
        return CreateSuccessResponse<object>(new { });
    }

    protected string CreateSuccessResponse(int createdId)
    {
        return CreateSuccessResponse<object>(new { createdId });
    }

    protected string CreateSuccessResponse<T>(T data)
        where T : class
    {
        var response = new RpcResponse<T> { Success = true, Data = data };
        var options = new JsonSerializerOptions { DefaultIgnoreCondition = System.Text.Json.Serialization.JsonIgnoreCondition.WhenWritingNull };
        var json = JsonSerializer.Serialize(response, options);
        _logger.LogInformation(
            "CreateSuccessResponse<{Type}>({Data}) -> {Response}",
            typeof(T).Name,
            data,
            json
        );
        return json;
    }

    protected string CreateErrorResponse<T>(string message, string kind = "UNKNOWN")
    {
        var response = new RpcResponse<T>
        {
            Success = false,
            Error = new RpcError { Message = message, Kind = kind },
        };
        var json = JsonSerializer.Serialize(response);
        _logger.LogInformation(
            "CreateErrorResponse<{Type}>({Message}, {Kind}) -> {Response}",
            typeof(T).Name,
            message,
            kind,
            json
        );
        return json;
    }

    protected string CreateErrorResponse(Exception ex)
    {
        var kind = ex switch
        {
            KeyNotFoundException => RpcErrorKind.NOT_FOUND.ToString(),
            InvalidOperationException => RpcErrorKind.VALIDATION.ToString(),
            DbUpdateException dbEx
                when dbEx.InnerException is PostgresException pgEx
                    && (pgEx.SqlState == "23505" || pgEx.SqlState == "23503") =>
                RpcErrorKind.VALIDATION.ToString(),
            _ => "FATAL",
        };

        var message = ex switch
        {
            DbUpdateException dbEx when dbEx.InnerException is PostgresException pgEx =>
                pgEx.MessageText,
            _ => ex.Message,
        };

        var response = new RpcResponse
        {
            Success = false,
            Error = new RpcError { Message = message, Kind = kind },
        };
        var json = JsonSerializer.Serialize(response);
        _logger.LogInformation(
            "CreateErrorResponse({ExType}, {Message}, {Kind}) -> {Response}",
            ex.GetType().Name,
            message,
            kind,
            json
        );
        return json;
    }

    #endregion
}
