namespace TodoApp.Shared.Messages;

public record CreateUserMessage(string Username, string Email);

public sealed class UpdateUserMessage
{
   public int Id { get; set; }
   public required UpdateUserData Data { get; set; }
}

public class UpdateUserData
{
   public string? Username { get; set; }
   public string? Email { get; set; }
}

public record DeleteUserMessage(int Id);

public record GetAllUsersMessage;

public record GetUserByIdMessage(int Id);
