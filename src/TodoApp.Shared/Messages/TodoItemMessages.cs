using System.ComponentModel.DataAnnotations;
using TodoApp.Shared.Validation;

namespace TodoApp.Shared.Messages;

// MVC reads validation constraints for records from the primary-constructor parameters; attributes
// targeted at the generated properties make model binding throw.
public record CreateTodoItemMessage(
   [Required] string Title,
   string Description,
   [Range(1, int.MaxValue)] int UserId);

public sealed class UpdateTodoItemMessage
{
   public int Id { get; set; }
   public required UpdateTodoItemData Data { get; set; }
}

public class UpdateTodoItemData
{
   [NotWhitespace]
   public string? Title { get; set; }

   public string? Description { get; set; }
   public bool? IsCompleted { get; set; }
}

public record DeleteTodoItemMessage(int Id);

public record GetTodosByUserIdMessage(int UserId);

public record GetTodoItemByIdMessage(int Id);
