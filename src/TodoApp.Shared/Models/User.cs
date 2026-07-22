namespace TodoApp.Shared.Models;

// Username and Email are unique; the constraints and the one-to-many relationship with
// TodoItems are configured in TodoDbContext (Fluent API), not via attributes here.
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
