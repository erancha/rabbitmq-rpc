using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WorkerService.Data;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// Consumes one queue and answers each delivery on the caller's reply queue.
///
/// Core responsibilities:
/// - Holds consumption until DbInitializationSignal completes, so no message is processed
///   against an unmigrated database
/// - Draws one message at a time (prefetch 1), so replicas compete for work by availability
/// - Settles every delivery exactly once: acked after processing, nacked to the dead-letter
///   exchange on failure, and never re-settled afterwards
/// - Drops requests whose client-supplied deadline has already passed, unless the request opted
///   into execution past its timeout
/// - Maps exceptions to RPC error kinds, exposing only domain-authored text to clients
/// </summary>
public abstract class BaseMessageHandler : IHostedService, IDisposable
{
    protected readonly IModel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    protected readonly ILogger _logger;
    protected readonly string _queueName;

    protected BaseMessageHandler(
        string queueName,
        IModel channel,
        IServiceScopeFactory scopeFactory,
        ILogger logger,
        DbInitializationSignal dbInitializationSignal
    )
    {
        _queueName = queueName;
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
        _dbInitializationSignal = dbInitializationSignal;
    }

    private readonly DbInitializationSignal _dbInitializationSignal;

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation(
            "Waiting for database initialization before starting {_queueName}",
            _queueName
        );
        await _dbInitializationSignal.Initialization;
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

            string? message = null;
            var acked = false;
            try
            {
                var messageType = ea.BasicProperties?.Type;
                if (string.IsNullOrEmpty(messageType))
                {
                    _logger.LogWarning("Message type is missing");
                    var errorResponse = CreateErrorResponse(new ValidationException("Message type is missing"), message ?? string.Empty);
                    SendRpcResponse(ea, errorResponse);
                    return;
                }

                if (HasRequestTimedOut(ea, messageType, out var elapsedSeconds, out var timeoutSeconds))
                {
                    var executeIfTimeout = ea.BasicProperties?.Headers?.TryGetValue(RpcHeaders.ExecuteIfTimeout, out var executeObj) == true
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

                message = Encoding.UTF8.GetString(ea.Body.ToArray());
                var idempotencyKey = GetHeaderString(ea, RpcHeaders.IdempotencyKey);

                var rpcResponse = await PrepareAndProcessMessage(messageType, message, idempotencyKey);
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
                acked = true;

                if (!HasRequestTimedOut(ea, messageType, out _, out _))
                    SendRpcResponse(ea, rpcResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing message: {Message}", message);

                // Each delivery is settled exactly once. Re-settling an acked tag is a
                // channel-level protocol error that closes the channel, so a failed reply
                // publish after the ack is only logged — the client observes a timeout.
                if (acked)
                    return;

                // Nack before publishing the error reply: the queue's dead-letter exchange
                // retains the message even if the reply publish fails.
                _channel.BasicNack(ea.DeliveryTag, false, false);

                string unescapedMessage = !string.IsNullOrWhiteSpace(message) ? UnescapeJsonMessage(message) : string.Empty;
                SendRpcResponse(ea, CreateErrorResponse(ex, unescapedMessage));
            }
        };

        _logger.LogInformation("Consumer started for {_queueName}", _queueName);
    }

    public Task StopAsync(CancellationToken cancellationToken)
    {
        _logger.LogInformation("Stopping consumer for {_queueName}", _queueName);
        return Task.CompletedTask;
    }

    /// <summary>
    /// Runs ProcessMessage with a fresh TodoDbContext resolved from a scope bounded to this message.
    /// </summary>
    private async Task<string> PrepareAndProcessMessage(string messageType, string message, string? idempotencyKey)
    {
        // A singleton handler shouldn't receive a scoped TodoDbContext in the ctor: it
        // needs a different dbContext per request, as DbContext isn't thread-safe.
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        // Reads carry no key, so there is nothing to deduplicate; only writes are keyed.
        if (string.IsNullOrEmpty(idempotencyKey))
            return await ProcessMessage(dbContext, messageType, message);

        return await ProcessWithIdempotency(dbContext, messageType, message, idempotencyKey);
    }

    /// <summary>
    /// Deduplicates a write. The marker is inserted in the same batch as the domain change, so the
    /// normal path adds one insert and no read; the marker's primary key is the guard. A duplicate
    /// (broker redelivery or HTTP retry — both carry the same key) makes that insert conflict, and
    /// the reply stored on the first marker is replayed instead of writing again. Marker and write
    /// share one transaction, committing or rolling back together. A key reused with a different
    /// request body is rejected, not replayed.
    /// </summary>
    private async Task<string> ProcessWithIdempotency(
        TodoDbContext dbContext,
        string messageType,
        string message,
        string idempotencyKey
    )
    {
        var requestHash = ComputeRequestHash(messageType, message);
        await using var transaction = await dbContext.Database.BeginTransactionAsync();

        var marker = new ProcessedMessage
        {
            Key = idempotencyKey,
            RequestHash = requestHash,
            CreatedAt = DateTime.UtcNow,
        };
        dbContext.ProcessedMessages.Add(marker);

        string response;
        try
        {
            // ProcessMessage's SaveChangesAsync flushes the marker and the domain write in one batch —
            // on the normal path, a single insert riding with the write, and no read.
            response = await ProcessMessage(dbContext, messageType, message);
        }
        catch (DbUpdateException ex) when (IsUniqueViolation(ex))
        {
            await transaction.RollbackAsync();

            // The batch violated a unique constraint — either the marker's key (a duplicate request)
            // or a domain constraint (a first-time request with, say, a taken username). A lookup
            // tells them apart, and it runs only on a conflict, never on the normal path.
            dbContext.ChangeTracker.Clear();
            var existing = await dbContext.ProcessedMessages.FindAsync(idempotencyKey);
            if (existing == null)
                throw; // a domain constraint, not a duplicate key — surfaces as a validation error

            if (existing.RequestHash != requestHash)
                throw new IdempotencyConflictException("Idempotency-Key was already used for a different request.");

            _logger.LogInformation("Replaying stored response for idempotency key {Key}", idempotencyKey);
            // A committed marker always carries its reply — both are written in the same transaction.
            return existing.ResponseJson!;
        }

        // Store the reply on the marker so a later duplicate replays it. The reply is known only
        // after the write (a create's id comes from the insert), so this is a second save.
        marker.ResponseJson = response;
        await dbContext.SaveChangesAsync();
        await transaction.CommitAsync();
        return response;
    }

    private static string ComputeRequestHash(string messageType, string message)
    {
        var bytes = System.Security.Cryptography.SHA256.HashData(
            Encoding.UTF8.GetBytes($"{messageType}\n{message}"));
        return Convert.ToHexString(bytes);
    }

    private static bool IsUniqueViolation(DbUpdateException ex) =>
        ex.InnerException is PostgresException { SqlState: "23505" };

    /// <summary>
    /// Reads a string-valued AMQP header, which the client library delivers as a byte array.
    /// Returns null when the header is absent.
    /// </summary>
    private static string? GetHeaderString(BasicDeliverEventArgs ea, string headerName)
    {
        if (ea.BasicProperties?.Headers?.TryGetValue(headerName, out var value) == true && value is byte[] bytes)
            return Encoding.UTF8.GetString(bytes);
        return null;
    }

    protected abstract Task<string> ProcessMessage(TodoDbContext dbContext, string messageType, string message);

    public virtual void Dispose()
    {
    }

    #region Request and Response Helpers

    /// <summary>
    /// Reports whether the caller has already stopped waiting, by measuring the request's age
    /// against the timeout it carried. Age is the difference between two clocks — the publisher's
    /// timestamp and this process's — so it is only as accurate as their skew. Deliveries with no
    /// timestamp or headers are treated as live, since their age is unknowable.
    /// </summary>
    private bool HasRequestTimedOut(
        BasicDeliverEventArgs ea,
        string messageType,
        out long elapsedSeconds,
        out int timeoutSeconds
    )
    {
        elapsedSeconds = 0;
        timeoutSeconds = 30; // Fallback when the request carries no timeout header

        if (ea.BasicProperties?.Timestamp == null || ea.BasicProperties?.Headers == null)
            return false;

        var requestTimestamp = ea.BasicProperties.Timestamp.UnixTime;
        timeoutSeconds = ea.BasicProperties.Headers.TryGetValue(RpcHeaders.TimeoutSeconds, out var timeoutObj)
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

    protected string CreateErrorResponse<T>(string message, string kind = RpcErrorKind.UNKNOWN)
    {
        var response = new RpcResponse<T>
        {
            Success = false,
            Error = new RpcError { Message = message, Kind = kind },
        };
        var json = JsonSerializer.Serialize(response);
        _logger.LogError(
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
        return CreateErrorResponse(ex, string.Empty);
    }

    protected string CreateErrorResponse(Exception ex, string requestMessage)
    {
        // Only DomainException messages are authored as client-facing text; anything else —
        // framework and driver messages, SQL constraint names — is replaced by a domain-level
        // message here. The full exception stays in the server-side log below.
        var (kind, message) = ex switch
        {
            DomainException domainEx => (domainEx.Kind, domainEx.Message),
            DbUpdateException { InnerException: PostgresException { SqlState: "23505" } } =>
                (RpcErrorKind.VALIDATION, "A record with this unique value already exists."),
            DbUpdateException { InnerException: PostgresException { SqlState: "23503" } } =>
                (RpcErrorKind.VALIDATION, "The referenced record does not exist."),
            _ => (RpcErrorKind.FATAL, "An unexpected error occurred while processing the request."),
        };

        var response = new RpcResponse<object>
        {
            Success = false,
            Error = new RpcError { Message = message, Kind = kind },
        };
        var json = JsonSerializer.Serialize(response);
        _logger.LogError(
            ex,
            "CreateErrorResponse({ExType}, {Kind}, {RequestMessage}) -> {Response}",
            ex.GetType().Name,
            kind,
            requestMessage,
            json
        );
        return json;
    }

    #endregion

    /// <summary>
    /// Collapses a request payload to single-line JSON for the error log. The payload is
    /// attacker-influenced and may not be JSON at all, so unparseable input is logged verbatim
    /// rather than rejected.
    /// </summary>
    private string UnescapeJsonMessage(string message)
    {
        try
        {
            using var doc = JsonDocument.Parse(message);
            return JsonSerializer.Serialize(doc.RootElement, new JsonSerializerOptions { WriteIndented = false });
        }
        catch
        {
            return message;
        }
    }
}
