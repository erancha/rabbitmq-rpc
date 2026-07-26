using Microsoft.AspNetCore.Mvc;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WebApi.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class UsersController : BaseApiController
{
    public UsersController(IRabbitMQMessageService messageService, ILogger<UsersController> logger)
        : base(messageService, logger)
    {
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserMessage message)
    {
        var localResponse = HandleLocalResponse(ValidateCreateUser(message));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc(
            message,
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: true,
            onSuccess: json => HandleRpcCreatedResponse(json, nameof(GetUserById))
        );
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserData data)
    {
        var localResponse = HandleLocalResponse(ValidateUpdateUser(id, data));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc(
            new UpdateUserMessage { Id = id, Data = data },
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: true,
            onSuccess: HandleRpcResponse
        );
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var localResponse = HandleLocalResponse(ValidateDeleteUser(id));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc(
            new DeleteUserMessage(id),
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: true,
            onSuccess: HandleRpcResponse
        );
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUsers() =>
        await ExecuteRpc(
            new GetAllUsersMessage(),
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: false,
            onSuccess: HandleRpcResponse
        );

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(int id)
    {
        var localResponse = HandleLocalResponse(ValidateGetUser(id));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc(
            new GetUserByIdMessage(id),
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: false,
            onSuccess: HandleRpcResponse
        );
    }

    private LocalValidationResult ValidateCreateUser(CreateUserMessage message)
    {
        if (message == null)
            return new LocalValidationResult(false, "Message cannot be null");

        if (string.IsNullOrWhiteSpace(message.Username))
            return new LocalValidationResult(false, "Username cannot be empty");

        if (string.IsNullOrWhiteSpace(message.Email))
            return new LocalValidationResult(false, "Email cannot be empty");

        if (!IsValidEmail(message.Email))
            return new LocalValidationResult(false, "Invalid email format");

        return new LocalValidationResult(true);
    }

    private LocalValidationResult ValidateUpdateUser(int id, UpdateUserData data)
    {
        if (id <= 0)
            return new LocalValidationResult(false, "Invalid user ID");

        if (data == null)
            return new LocalValidationResult(false, "Update data cannot be null");

        if (data.Email != null && !IsValidEmail(data.Email))
            return new LocalValidationResult(false, "Invalid email format");

        return new LocalValidationResult(true);
    }

    private LocalValidationResult ValidateDeleteUser(int id)
    {
        if (id <= 0)
            return new LocalValidationResult(false, "Id must be greater than 0");

        return new LocalValidationResult(true);
    }

    private LocalValidationResult ValidateGetUser(int id)
    {
        if (id <= 0)
            return new LocalValidationResult(false, "Id must be greater than 0");

        return new LocalValidationResult(true);
    }

    private static bool IsValidEmail(string email)
    {
        try
        {
            var addr = new System.Net.Mail.MailAddress(email);
            return addr.Address == email;
        }
        catch
        {
            return false;
        }
    }
}
