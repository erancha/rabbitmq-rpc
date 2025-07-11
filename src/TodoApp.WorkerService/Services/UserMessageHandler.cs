using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Npgsql;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Configuration;
using TodoApp.Shared.Data;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WorkerService.Helpers;

namespace TodoApp.WorkerService.Services;

public class UserMessageHandler : IDisposable
{
    private const string CurrentQueueName = Configuration.QueueConfiguration.UsersQueueName;
    private readonly IModel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<UserMessageHandler> _logger;

    public UserMessageHandler(
        IModel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger
    )
    {
        _channel = channel;
        _scopeFactory = scopeFactory;
        _logger = logger;
    }

    public void StartConsuming()
    {
        _logger.LogInformation(
            "Starting to consume messages from {CurrentQueueName}",
            CurrentQueueName
        );
        _channel.BasicQos(prefetchSize: 0, prefetchCount: 1, global: false); // Limit to processing one message at a time per consumer
        var consumer = new EventingBasicConsumer(_channel);
        _channel.BasicConsume(queue: CurrentQueueName, autoAck: false, consumer: consumer);

        consumer.Received += async (model, ea) =>
        {
            try
            {
                var messageType = ea.BasicProperties?.Type;
                if (string.IsNullOrEmpty(messageType))
                {
                    _logger.LogWarning("Message type is missing");
                    var errorResponse = RpcResponseHelper.CreateErrorResponse(
                        new InvalidOperationException("Message type is missing")
                    );
                    RpcResponseHelper.SendRpcResponse(_channel, ea, errorResponse);
                    return;
                }

                var message = Encoding.UTF8.GetString(ea.Body.ToArray());
                var rpcResponse = await ProcessMessage(messageType, message);
                _channel.BasicAck(ea.DeliveryTag, multiple: false);
                RpcResponseHelper.SendRpcResponse(_channel, ea, rpcResponse);
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error processing user message");
                var rpcResponse = RpcResponseHelper.CreateErrorResponse(ex);
                RpcResponseHelper.SendRpcResponse(_channel, ea, rpcResponse);
                _channel.BasicNack(ea.DeliveryTag, false, false); // FFU: Only requeue on approved errors.
            }
        };

        _logger.LogInformation("Consumer started for {CurrentQueueName}", CurrentQueueName);
    }

    private async Task<string> ProcessMessage(string messageType, string message)
    {
        try
        {
            _logger.LogInformation("Processing message of type {MessageType}", messageType);

            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

            switch (messageType)
            {
                case nameof(CreateUserMessage):
                    var createMessage = JsonSerializer.Deserialize<CreateUserMessage>(message);
                    if (createMessage != null)
                    {
                        var id = await CreateUser(dbContext, createMessage);
                        return RpcResponseHelper.CreateSuccessResponse(id);
                    }
                    else
                        throw new InvalidOperationException(
                            $"Deserialization failed for CreateUserMessage. Message: {message}"
                        );

                case nameof(UpdateUserMessage):
                    var updateMessage = JsonSerializer.Deserialize<UpdateUserMessage>(message);
                    if (updateMessage != null)
                        await UpdateUser(dbContext, updateMessage);
                    else
                        throw new InvalidOperationException(
                            $"Deserialization failed for UpdateUserMessage. Message: {message}"
                        );
                    break;

                case nameof(DeleteUserMessage):
                    var deleteMessage = JsonSerializer.Deserialize<DeleteUserMessage>(message);
                    if (deleteMessage != null)
                        await DeleteUser(dbContext, deleteMessage);
                    else
                        throw new InvalidOperationException(
                            $"Deserialization failed for DeleteUserMessage. Message: {message}"
                        );
                    break;

                default:
                    _logger.LogWarning("Unknown message type: {MessageType}", messageType);
                    throw new InvalidOperationException($"Unknown message type: {messageType}");
            }

            return RpcResponseHelper.CreateSuccessResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user message");
            return RpcResponseHelper.CreateErrorResponse(ex);
        }
    }

    private async Task<int> CreateUser(TodoDbContext dbContext, CreateUserMessage message)
    {
        _logger.LogInformation("Creating user with username {Username}", message.Username);

        var user = new User
        {
            Username = message.Username,
            Email = message.Email,
            CreatedAt = DateTime.UtcNow,
        };

        dbContext.Users.Add(user);
        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Created user with ID {UserId}", user.Id);
        return user.Id;
    }

    private async Task UpdateUser(TodoDbContext dbContext, UpdateUserMessage message)
    {
        var user = await dbContext.Users.FindAsync(message.Id);
        if (user == null)
            throw new KeyNotFoundException($"User with ID {message.Id} not found");

        if (message.Data.Username != null)
            user.Username = message.Data.Username;

        if (message.Data.Email != null)
            user.Email = message.Data.Email;

        await dbContext.SaveChangesAsync();
    }

    private async Task DeleteUser(TodoDbContext dbContext, DeleteUserMessage message)
    {
        var user = await dbContext.Users.FindAsync(message.Id);
        if (user == null)
        {
            throw new KeyNotFoundException($"User with ID {message.Id} not found");
        }

        dbContext.Users.Remove(user);
        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Deleted user with ID {UserId}", user.Id);
    }

    public void Dispose() { }
}
