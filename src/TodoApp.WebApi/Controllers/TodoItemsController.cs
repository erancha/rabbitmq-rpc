using System.ComponentModel.DataAnnotations;
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
    public async Task<ActionResult<CreatedResponse>> CreateTodoItem([FromBody] CreateTodoItemMessage message) =>
        await ExecuteRpc<CreateTodoItemMessage, CreatedResponse>(
            message,
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: true,
            onSuccess: created => CreatedAtAction(nameof(GetTodoItemById), new { id = created.CreatedId }, created)
        );

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateTodoItem([Range(1, int.MaxValue)] int id, [FromBody] UpdateTodoItemData data) =>
        await ExecuteRpc(
            new UpdateTodoItemMessage { Id = id, Data = data },
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: true
        );

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteTodoItem([Range(1, int.MaxValue)] int id) =>
        await ExecuteRpc(
            new DeleteTodoItemMessage(id),
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: true
        );

    [HttpGet("{id}")]
    public async Task<ActionResult<TodoItemResponse>> GetTodoItemById([Range(1, int.MaxValue)] int id) =>
        await ExecuteRpc<GetTodoItemByIdMessage, TodoItemResponse>(
            new GetTodoItemByIdMessage(id),
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: false
        );

    [HttpGet("user/{userId}")]
    public async Task<ActionResult<List<TodoItemResponse>>> GetTodosByUserId([Range(1, int.MaxValue)] int userId) =>
        await ExecuteRpc<GetTodosByUserIdMessage, List<TodoItemResponse>>(
            new GetTodosByUserIdMessage(userId),
            RabbitMQShared.RoutingKeys.Todo,
            executeIfTimeout: false
        );
}
