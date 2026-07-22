namespace TodoApp.Shared.Models;

// Deletes are soft: rows are kept with IsDeleted/DeletedAt set, and readers must filter on
// IsDeleted. The UserId foreign key (cascade delete) is configured in TodoDbContext (Fluent API).
public class TodoItem
{
    public int Id { get; set; }
    public string Title { get; set; } = string.Empty;
    public string Description { get; set; } = string.Empty;
    public bool IsCompleted { get; set; }
    public DateTime CreatedAt { get; set; }
    public DateTime? CompletedAt { get; set; }
    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }
    
    public int UserId { get; set; }
}
