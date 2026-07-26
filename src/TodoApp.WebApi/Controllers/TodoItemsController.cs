using Microsoft.AspNetCore.Mvc;
using TodoApp.Shared.Messages;
using TodoApp.WebApi.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.WebApi.Controllers;

[ApiController]
[Route("api/v1/[controller]")]
public class TodoItemsController : BaseApiController
{
    public TodoItemsController(IRabbitMQMessageService messageService, ILogger<TodoItemsController> logger)
        : base(messageService, logger)
    {
    }

    [HttpPost]
    [ProducesResponseType(typeof(CreatedResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedResponse>> CreateTodoItem([FromBody] CreateTodoItemMessage message)
    {
        var localResponse = HandleLocalResponse(ValidateCreateTodoItem(message));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc<CreateTodoItemMessage, CreatedResponse>(
            message,
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: true,
            onSuccess: created => CreatedAtAction(nameof(GetTodoItemById), new { id = created.CreatedId }, created)
        );
    }

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTodoItem(int id, [FromBody] UpdateTodoItemData data)
    {
        var localResponse = HandleLocalResponse(ValidateUpdateTodoItem(id, data));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc(
            new UpdateTodoItemMessage { Id = id, Data = data },
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: true
        );
    }

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTodoItem(int id)
    {
        var localResponse = HandleLocalResponse(ValidateDeleteTodoItem(id));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc(
            new DeleteTodoItemMessage(id),
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: true
        );
    }

    [HttpGet("{id}")]
    public async Task<ActionResult<TodoItemResponse>> GetTodoItemById(int id)
    {
        var localResponse = HandleLocalResponse(ValidateGetTodoItem(id));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc<GetTodoItemByIdMessage, TodoItemResponse>(
            new GetTodoItemByIdMessage(id),
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: false
        );
    }

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<List<TodoItemResponse>>> GetTodosByUserId(int userId)
    {
        var localResponse = HandleLocalResponse(ValidateGetTodosByUserId(userId));
        if (localResponse != null)
            return localResponse;

        return await ExecuteRpc<GetTodosByUserIdMessage, List<TodoItemResponse>>(
            new GetTodosByUserIdMessage(userId),
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: false
        );
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

    private LocalValidationResult ValidateGetTodoItem(int id)
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
