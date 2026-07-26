using System.ComponentModel.DataAnnotations;
using Microsoft.AspNetCore.Mvc;
using TodoApp.Shared.Messages;
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
    [ProducesResponseType(typeof(CreatedResponse), StatusCodes.Status201Created)]
    public async Task<ActionResult<CreatedResponse>> CreateUser([FromBody] CreateUserMessage message) =>
        await ExecuteRpc<CreateUserMessage, CreatedResponse>(
            message,
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: true,
            onSuccess: created => CreatedAtAction(nameof(GetUserById), new { id = created.CreatedId }, created)
        );

    [HttpPut("{id}")]
    public async Task<IActionResult> UpdateUser([Range(1, int.MaxValue)] int id, [FromBody] UpdateUserData data) =>
        await ExecuteRpc(
            new UpdateUserMessage { Id = id, Data = data },
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: true
        );

    [HttpDelete("{id}")]
    public async Task<IActionResult> DeleteUser([Range(1, int.MaxValue)] int id) =>
        await ExecuteRpc(
            new DeleteUserMessage(id),
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: true
        );

    [HttpGet]
    public async Task<ActionResult<GetAllUsersResponse>> GetAllUsers() =>
        await ExecuteRpc<GetAllUsersMessage, GetAllUsersResponse>(
            new GetAllUsersMessage(),
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: false
        );

    [HttpGet("{id}")]
    public async Task<ActionResult<GetUserByIdResponse>> GetUserById([Range(1, int.MaxValue)] int id) =>
        await ExecuteRpc<GetUserByIdMessage, GetUserByIdResponse>(
            new GetUserByIdMessage(id),
            RabbitMQShared.RoutingKeys.User,
            executeIfTimeout: false
        );
}
