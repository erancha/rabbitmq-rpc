using System.Text;
using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using RabbitMQ.Client.Events;
using TodoApp.Shared.Configuration;
using TodoApp.Shared.Data;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WorkerService.Helpers;

namespace TodoApp.WorkerService.Services;

public class TodoItemMessageHandler : IDisposable
{
    private const string CurrentQueueName = Configuration.QueueConfiguration.TodosQueueName;
    private readonly IModel _channel;
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<TodoItemMessageHandler> _logger;

    public TodoItemMessageHandler(
        IModel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<TodoItemMessageHandler> logger
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
                var body = ea.Body.ToArray();
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
                _logger.LogError(ex, "Error processing todo item message");
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
                case nameof(CreateTodoItemMessage):
                    var createMessage = JsonSerializer.Deserialize<CreateTodoItemMessage>(message);
                    if (createMessage != null)
                    {
                        var id = await CreateTodoItem(dbContext, createMessage);
                        return RpcResponseHelper.CreateSuccessResponse(id);
                    }
                    else
                        throw new InvalidOperationException(
                            $"Deserialization failed for CreateTodoItemMessage. Message: {message}"
                        );

                case nameof(UpdateTodoItemMessage):
                    var updateMessage = JsonSerializer.Deserialize<UpdateTodoItemMessage>(message);
                    if (updateMessage != null)
                        await UpdateTodoItem(dbContext, updateMessage);
                    else
                        throw new InvalidOperationException(
                            $"Deserialization failed for UpdateTodoItemMessage. Message: {message}"
                        );
                    break;

                case nameof(DeleteTodoItemMessage):
                    var deleteMessage = JsonSerializer.Deserialize<DeleteTodoItemMessage>(message);
                    if (deleteMessage != null)
                        await DeleteTodoItem(dbContext, deleteMessage);
                    else
                        throw new InvalidOperationException(
                            $"Deserialization failed for DeleteTodoItemMessage. Message: {message}"
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
            _logger.LogError(ex, "Error processing todo item message");
            return RpcResponseHelper.CreateErrorResponse(ex);
        }
    }

    private async Task<int> CreateTodoItem(TodoDbContext dbContext, CreateTodoItemMessage message)
    {
        _logger.LogInformation("Creating todo item for user {UserId}", message.UserId);

        var todoItem = new TodoItem
        {
            Title = message.Title,
            Description = message.Description,
            UserId = message.UserId,
            CreatedAt = DateTime.UtcNow,
            IsCompleted = false,
            IsDeleted = false,
        };

        dbContext.TodoItems.Add(todoItem);
        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Created todo item with ID {TodoItemId}", todoItem.Id);
        return todoItem.Id;
    }

    private async Task UpdateTodoItem(TodoDbContext dbContext, UpdateTodoItemMessage message)
    {
        var todoItem = await dbContext.TodoItems.FindAsync(message.Id);
        if (todoItem == null || todoItem.IsDeleted)
        {
            throw new KeyNotFoundException($"TodoItem with ID {message.Id} not found");
        }

        if (message.Data.Title != null)
            todoItem.Title = message.Data.Title;
        if (message.Data.Description != null)
            todoItem.Description = message.Data.Description;
        if (message.Data.IsCompleted.HasValue)
        {
            todoItem.IsCompleted = message.Data.IsCompleted.Value;
            if (message.Data.IsCompleted.Value && !todoItem.CompletedAt.HasValue)
            {
                todoItem.CompletedAt = DateTime.UtcNow;
            }
            else if (!message.Data.IsCompleted.Value)
            {
                todoItem.CompletedAt = null;
            }
        }

        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Updated todo item with ID {TodoItemId}", todoItem.Id);
    }

    private async Task DeleteTodoItem(TodoDbContext dbContext, DeleteTodoItemMessage message)
    {
        var todoItem = await dbContext.TodoItems.FindAsync(message.Id);
        if (todoItem == null || todoItem.IsDeleted)
        {
            throw new KeyNotFoundException($"TodoItem with ID {message.Id} not found");
        }

        todoItem.IsDeleted = true;
        todoItem.DeletedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Deleted todo item with ID {TodoItemId}", todoItem.Id);
    }

    public void Dispose() { }
}
