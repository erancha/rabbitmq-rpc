namespace TodoApp.Shared.Messages;

public record CreateTodoItemMessage(string Title, string Description, int UserId);

public sealed class UpdateTodoItemMessage
{
   public int Id { get; set; }
   public required UpdateTodoItemData Data { get; set; }
}

public class UpdateTodoItemData
{
   public string? Title { get; set; }
   public string? Description { get; set; }
   public bool? IsCompleted { get; set; }
}

public record DeleteTodoItemMessage(int Id);
