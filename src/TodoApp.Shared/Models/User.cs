namespace TodoApp.Shared.Models;

// Notes:
// - Primary key: Id (integer)
// - Unique constraints: Username, Email (configured in TodoDbContext using Fluent API)
// - Timestamps: CreatedAt
// - One-to-many relationship with TodoItems (configured in TodoDbContext using Fluent API)
public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
}
