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
public class TodoItemsController : BaseApiController
{
    private readonly IRabbitMQMessageService _messageService;
    private readonly ILogger<TodoItemsController> _logger;

    public TodoItemsController(
        IRabbitMQMessageService messageService,
        ILogger<TodoItemsController> logger
    )
        : base(logger)
    {
        _messageService = messageService;
        _logger = logger;
    }

    [HttpPost]
    public async Task<IActionResult> CreateTodoItem([FromBody] CreateTodoItemMessage message)
    {
        var validation = ValidateCreateTodoItem(message);
        var localResponse = HandleLocalResponse(validation);
        if (localResponse != null)
            return localResponse;

        try
        {
            var result = await _messageService.PublishMessageRpc<CreateTodoItemMessage>(
                message,
                RabbitMQShared.RoutingKeys.Todo
            );
            return HandleRpcResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing create todo item message");
            return StatusCode(500, "Error processing request");
        }
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTodoItem(int id, [FromBody] UpdateTodoItemData data)
    {
        var validation = ValidateUpdateTodoItem(id, data);
        var localResponse = HandleLocalResponse(validation);
        if (localResponse != null)
            return localResponse;

        var message = new UpdateTodoItemMessage { Id = id, Data = data };
        try
        {
            var result = await _messageService.PublishMessageRpc<UpdateTodoItemMessage>(
                message,
                RabbitMQShared.RoutingKeys.Todo
            );
            return HandleRpcResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing update todo item message");
            return StatusCode(500, "Error processing request");
        }
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTodoItem(int id)
    {
        var validation = ValidateDeleteTodoItem(id);
        var localResponse = HandleLocalResponse(validation);
        if (localResponse != null)
            return localResponse;

        try
        {
            var message = new DeleteTodoItemMessage(id);
            var result = await _messageService.PublishMessageRpc<DeleteTodoItemMessage>(
                message,
                RabbitMQShared.RoutingKeys.Todo
            );
            return HandleRpcResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing delete todo item message");
            return StatusCode(500, "Error processing request");
        }
    }

    [HttpGet("user/{userId}")]
    public async Task<IActionResult> GetTodosByUserId(int userId)
    {
        var validation = ValidateGetTodosByUserId(userId);
        var localResponse = HandleLocalResponse(validation);
        if (localResponse != null)
            return localResponse;

        try
        {
            var message = new GetTodosByUserIdMessage(userId);
            var result = await _messageService.PublishMessageRpc<GetTodosByUserIdMessage>(
                message,
                RabbitMQShared.RoutingKeys.Todo
            );
            return HandleRpcResponse(result);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error publishing get todos by user id message");
            return StatusCode(500, "Error processing request");
        }
    }

    private LocalValidationResult ValidateCreateTodoItem(CreateTodoItemMessage message)
    {
        if (message == null)
            return new LocalValidationResult(false, "Message cannot be null");

        if (message.UserId <= 0)
            return new LocalValidationResult(false, "Invalid user ID");

        if (string.IsNullOrWhiteSpace(message.Title))
            return new LocalValidationResult(false, "Title cannot be empty");

        return new LocalValidationResult(true);
    }

    private LocalValidationResult ValidateUpdateTodoItem(int id, UpdateTodoItemData data)
    {
        if (id <= 0)
            return new LocalValidationResult(false, "Invalid todo item ID");

        if (data == null)
            return new LocalValidationResult(false, "Update data cannot be null");

        // Only validate title format if it's provided
        if (data.Title != null && string.IsNullOrWhiteSpace(data.Title))
            return new LocalValidationResult(false, "Title cannot be empty when provided");

        return new LocalValidationResult(true);
    }

    private LocalValidationResult ValidateDeleteTodoItem(int id)
    {
        if (id <= 0)
            return new LocalValidationResult(false, "Id must be greater than 0");

        return new LocalValidationResult(true);
    }

    private LocalValidationResult ValidateGetTodosByUserId(int userId)
    {
        if (userId <= 0)
            return new LocalValidationResult(false, "User Id must be greater than 0");

        return new LocalValidationResult(true);
    }
}
