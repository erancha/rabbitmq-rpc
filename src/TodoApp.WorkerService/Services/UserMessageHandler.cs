using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WorkerService.Data;

namespace TodoApp.WorkerService.Services;

public class UserMessageHandler : BaseMessageHandler
{
    private const string CurrentQueueName = Configuration.RabbitMQConfig.UsersQueueName;

    public UserMessageHandler(
        IModel channel,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        InitializationSignal initializationSignal
    )
        : base(CurrentQueueName, channel, scopeFactory, logger, initializationSignal) { }

    protected override async Task<string> ProcessMessage(string messageType, string message)
    {
        try
        {
            _logger.LogInformation("Processing message of type {MessageType}", messageType);

            // Create a new scope to get a fresh DbContext instance for each message
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

            switch (messageType)
            {
                case nameof(CreateUserMessage):
                    var createMessage = JsonSerializer.Deserialize<CreateUserMessage>(message);
                    if (createMessage != null)
                    {
                        var id = await CreateUser(dbContext, createMessage);
                        return CreateSuccessResponse(id);
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

                case nameof(GetAllUsersMessage):
                    try
                    {
                        var users = await dbContext.Users.ToListAsync();
                        var response = new GetAllUsersResponse { Users = users };
                        return CreateSuccessResponse(response);
                    }
                    catch (Exception ex)
                    {
                        _logger.LogError(ex, "Error getting all users");
                        return CreateErrorResponse<GetAllUsersResponse>(
                            "Database error",
                            "INTERNAL_ERROR"
                        );
                    }

                case nameof(GetUserByIdMessage):
                    var getUserMessage = JsonSerializer.Deserialize<GetUserByIdMessage>(message);
                    if (getUserMessage != null)
                    {
                        try
                        {
                            var user = await GetUserById(dbContext, getUserMessage);
                            if (user == null)
                                return CreateErrorResponse<GetUserByIdResponse>(
                                    "User not found",
                                    RpcErrorKind.NOT_FOUND.ToString()
                                );
                            var userResponse = new GetUserByIdResponse { User = user };
                            return CreateSuccessResponse(userResponse);
                        }
                        catch (Exception ex)
                        {
                            _logger.LogError(
                                ex,
                                "Error getting user by id {Id}",
                                getUserMessage.Id
                            );
                            return CreateErrorResponse<GetUserByIdResponse>(
                                "Database error",
                                "INTERNAL_ERROR"
                            );
                        }
                    }
                    else
                        throw new InvalidOperationException(
                            $"Deserialization failed for GetUserByIdMessage. Message: {message}"
                        );

                default:
                    _logger.LogWarning("Unknown message type: {MessageType}", messageType);
                    throw new InvalidOperationException($"Unknown message type: {messageType}");
            }

            return CreateSuccessResponse();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error processing user message");
            return CreateErrorResponse(ex);
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

    private async Task<List<User>> GetAllUsers(TodoDbContext dbContext)
    {
        return await dbContext.Users.ToListAsync();
    }

    private async Task<User?> GetUserById(TodoDbContext dbContext, GetUserByIdMessage message)
    {
        return await dbContext.Users.FindAsync(message.Id);
    }

    public override void Dispose() { }
}
