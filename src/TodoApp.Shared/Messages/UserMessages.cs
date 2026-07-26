using System.ComponentModel.DataAnnotations;

namespace TodoApp.Shared.Messages;

// MVC reads validation constraints for records from the primary-constructor parameters; attributes
// targeted at the generated properties make model binding throw.
public record CreateUserMessage(
   [Required] string Username,
   [Required, EmailAddress] string Email);

public sealed class UpdateUserMessage
{
   public int Id { get; set; }
   public required UpdateUserData Data { get; set; }
}

public class UpdateUserData
{
   public string? Username { get; set; }

   [EmailAddress]
   public string? Email { get; set; }
}

public record DeleteUserMessage(int Id);

public record GetAllUsersMessage;

public record GetUserByIdMessage(int Id);
