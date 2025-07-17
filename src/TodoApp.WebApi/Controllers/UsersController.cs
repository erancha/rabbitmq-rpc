using System.Text;
using System.Text.Json;
using Microsoft.AspNetCore.Mvc;
using RabbitMQ.Client;
using TodoApp.Shared.Messages;
using TodoApp.Shared.Models;
using TodoApp.WebApi.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class UsersController : BaseApiController
{
    private readonly IRabbitMQMessageService _messageService;
    private readonly ILogger<UsersController> _logger;

    public UsersController(IRabbitMQMessageService messageService, ILogger<UsersController> logger)
        : base(logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateUser([FromBody] CreateUserMessage message)
    {
        var validation = ValidateCreateUser(message);
        var localResponse = HandleLocalResponse(validation);
        if (localResponse != null)
            return localResponse;

        try
        {
            var responseJson = await _messageService.PublishMessageRpc<CreateUserMessage>(
                message,
                RabbitMQShared.RoutingKeys.User,
                executeIfTimeout: true
            );
            return HandleRpcResponse(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing create user message");
            return StatusCode(500, "Error processing request");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser(int id, [FromBody] UpdateUserData data)
    {
        var validation = ValidateUpdateUser(id, data);
        var localResponse = HandleLocalResponse(validation);
        if (localResponse != null)
            return localResponse;

        var message = new UpdateUserMessage { Id = id, Data = data };
        try
        {
            var responseJson = await _messageService.PublishMessageRpc<UpdateUserMessage>(
                message,
                RabbitMQShared.RoutingKeys.User,
                executeIfTimeout: true
            );
            return HandleRpcResponse(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing update user message");
            return StatusCode(500, "Error processing request");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser(int id)
    {
        var validation = ValidateDeleteUser(id);
        var localResponse = HandleLocalResponse(validation);
        if (localResponse != null)
            return localResponse;

        try
        {
            var message = new DeleteUserMessage(id);
            var responseJson = await _messageService.PublishMessageRpc<DeleteUserMessage>(
                message,
                RabbitMQShared.RoutingKeys.User,
                executeIfTimeout: true
            );
            return HandleRpcResponse(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing delete user message");
            return StatusCode(500, "Error processing request");
        }
    }

    [HttpGet]
    public async Task<IActionResult> GetAllUsers()
    {
        try
        {
            var message = new GetAllUsersMessage();
            var responseJson = await _messageService.PublishMessageRpc<GetAllUsersMessage>(
                message,
                RabbitMQShared.RoutingKeys.User
            );
            return HandleRpcResponse(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing get all users message");
            return StatusCode(500, "Error processing request");
        }
    }

    [HttpGet("{id}")]
    public async Task<IActionResult> GetUserById(int id)
    {
        var validation = ValidateGetUser(id);
        var localResponse = HandleLocalResponse(validation);
        if (localResponse != null)
            return localResponse;

        try
        {
            var message = new GetUserByIdMessage(id);
            var responseJson = await _messageService.PublishMessageRpc<GetUserByIdMessage>(
                message,
                RabbitMQShared.RoutingKeys.User
            );
            return HandleRpcResponse(responseJson);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing get user by id message");
            return StatusCode(500, "Error processing request");
        }
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

        // Only validate email format if it's provided
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
