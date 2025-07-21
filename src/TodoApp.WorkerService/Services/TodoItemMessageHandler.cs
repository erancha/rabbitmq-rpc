using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WorkerService.Data;

namespace TodoApp.WorkerService.Services;

public class TodoItemMessageHandler : BaseMessageHandler
{
    private const string CurrentQueueName = Configuration.RabbitMQConfig.TodosQueueName;

    public TodoItemMessageHandler(
        IModel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<TodoItemMessageHandler> logger,
        InitializationSignal initializationSignal
    )
        : base(CurrentQueueName, channel, scopeFactory, logger, initializationSignal) { }

    protected override async Task<string> ProcessMessage(string messageType, string message)
    {
        _logger.LogInformation("Processing message of type {MessageType}", messageType);

        // Create a new scope to get a fresh DbContext instance for each message
        using var scope = _scopeFactory.CreateScope();
        var dbContext = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

        switch (messageType)
        {
            case nameof(CreateTodoItemMessage):
                var createMessage = JsonSerializer.Deserialize<CreateTodoItemMessage>(message);
                if (createMessage != null)
                {
                    var id = await CreateTodoItem(dbContext, createMessage);
                    return CreateSuccessResponse(id);
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

            case nameof(GetTodosByUserIdMessage):
                var getTodosMessage = JsonSerializer.Deserialize<GetTodosByUserIdMessage>(
                    message
                );
                if (getTodosMessage != null)
                {
                    var todos = await GetTodosByUserId(dbContext, getTodosMessage);
                    return CreateSuccessResponse(todos);
                }
                else
                    throw new InvalidOperationException(
                        $"Deserialization failed for GetTodosByUserIdMessage. Message: {message}"
                    );

            default:
                _logger.LogWarning("Unknown message type: {MessageType}", messageType);
                throw new InvalidOperationException($"Unknown message type: {messageType}");
        }

        return CreateSuccessResponse();
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
            throw new KeyNotFoundException($"TodoItem with ID {message.Id} not found");

        if (message.Data.Title != null)
            todoItem.Title = message.Data.Title;
        if (message.Data.Description != null)
            todoItem.Description = message.Data.Description;
        if (message.Data.IsCompleted.HasValue)
        {
            todoItem.IsCompleted = message.Data.IsCompleted.Value;
            if (message.Data.IsCompleted.Value && !todoItem.CompletedAt.HasValue)
                todoItem.CompletedAt = DateTime.UtcNow;
            else if (!message.Data.IsCompleted.Value)
                todoItem.CompletedAt = null;
        }

        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Updated todo item with ID {TodoItemId}", todoItem.Id);
    }

    private async Task DeleteTodoItem(TodoDbContext dbContext, DeleteTodoItemMessage message)
    {
        var todoItem = await dbContext.TodoItems.FindAsync(message.Id);
        if (todoItem == null || todoItem.IsDeleted)
            throw new KeyNotFoundException($"TodoItem with ID {message.Id} not found");

        todoItem.IsDeleted = true;
        todoItem.DeletedAt = DateTime.UtcNow;
        await dbContext.SaveChangesAsync();
        _logger.LogInformation("Deleted todo item with ID {TodoItemId}", todoItem.Id);
    }

    private async Task<List<TodoItemResponse>> GetTodosByUserId(
        TodoDbContext dbContext,
        GetTodosByUserIdMessage message
    )
    {
        var todos = await dbContext
            .TodoItems.Where(t => t.UserId == message.UserId && !t.IsDeleted)
            .OrderByDescending(t => t.CreatedAt)
            .ToListAsync();

        return todos
            .Select(t => new TodoItemResponse
            {
                Id = t.Id,
                Title = t.Title,
                Description = t.Description,
                IsCompleted = t.IsCompleted,
                CreatedAt = t.CreatedAt,
                CompletedAt = t.CompletedAt,
                IsDeleted = t.IsDeleted,
                DeletedAt = t.DeletedAt,
            })
            .ToList();
    }

    public override void Dispose() { }
}
