using System.Text.Json;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WorkerService.Data;

namespace TodoApp.WorkerService.Services;

public class UserMessageHandler : BaseMessageHandler
{
    private const string CurrentQueueName = Configuration.RabbitMQConfig.UsersQueueName;
    private readonly string _instanceId;

    public UserMessageHandler(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(CurrentQueueName, channelPool.Get(), scopeFactory, logger, dbInitializationSignal) 
    {
        _instanceId = Guid.NewGuid().ToString("N")[..8]; // Short unique ID
    }

    protected override async Task<string> ProcessMessage(string messageType, string message)
    {
        _logger.LogInformation("[Instance {InstanceId}] Processing message of type {MessageType}", _instanceId, messageType);

        // Get a fresh DbContext instance for each message
        var dbContext = _scopeFactory.CreateScope().ServiceProvider.GetRequiredService<TodoDbContext>();

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
                var users = await GetAllUsers(dbContext);
                var response = new GetAllUsersResponse { Users = users };
                return CreateSuccessResponse(response);

            case nameof(GetUserByIdMessage):
                var getUserMessage = JsonSerializer.Deserialize<GetUserByIdMessage>(message);
                if (getUserMessage != null)
                {
                    var user = await GetUserById(dbContext, getUserMessage) ?? throw new KeyNotFoundException($"User with ID {getUserMessage.Id} not found");
                    var userResponse = new GetUserByIdResponse { User = user };
                    return CreateSuccessResponse(userResponse);
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
        _logger.LogInformation("[Instance {InstanceId}] Deleted user with ID {UserId}", _instanceId, user.Id);
    }

    private async Task<List<User>> GetAllUsers(TodoDbContext dbContext)
    {
        return await dbContext.Users.OrderBy(u => u.Id).Take(100).ToListAsync();
    }

    private async Task<User?> GetUserById(TodoDbContext dbContext, GetUserByIdMessage message)
    {
        return await dbContext.Users.FindAsync(message.Id);
    }

    public override void Dispose() { }
}

#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#region UserMessageHandler Wrapper Classes (2-20)
public class UserMessageHandler2 : UserMessageHandler
{
    public UserMessageHandler2(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler3 : UserMessageHandler
{
    public UserMessageHandler3(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler4 : UserMessageHandler
{
    public UserMessageHandler4(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler5 : UserMessageHandler
{
    public UserMessageHandler5(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler6 : UserMessageHandler
{
    public UserMessageHandler6(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler7 : UserMessageHandler
{
    public UserMessageHandler7(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler8 : UserMessageHandler
{
    public UserMessageHandler8(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler9 : UserMessageHandler
{
    public UserMessageHandler9(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler10 : UserMessageHandler
{
    public UserMessageHandler10(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler11 : UserMessageHandler
{
    public UserMessageHandler11(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler12 : UserMessageHandler
{
    public UserMessageHandler12(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler13 : UserMessageHandler
{
    public UserMessageHandler13(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler14 : UserMessageHandler
{
    public UserMessageHandler14(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler15 : UserMessageHandler
{
    public UserMessageHandler15(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler16 : UserMessageHandler
{
    public UserMessageHandler16(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler17 : UserMessageHandler
{
    public UserMessageHandler17(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler18 : UserMessageHandler
{
    public UserMessageHandler18(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler19 : UserMessageHandler
{
    public UserMessageHandler19(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}

public class UserMessageHandler20 : UserMessageHandler
{
    public UserMessageHandler20(
        ObjectPool<IModel> channelPool,
        IServiceScopeFactory scopeFactory,
        ILogger<UserMessageHandler> logger,
        DbInitializationSignal dbInitializationSignal)
        : base(channelPool, scopeFactory, logger, dbInitializationSignal)
    {
    }
}
#endregion
#pragma warning restore CS1591
