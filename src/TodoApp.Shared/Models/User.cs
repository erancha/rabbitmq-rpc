namespace TodoApp.Shared.Models;

public class User
{
    public int Id { get; set; }
    public string Username { get; set; } = string.Empty;
    public string Email { get; set; } = string.Empty;
    public DateTime CreatedAt { get; set; }
    public virtual ICollection<TodoItem> Items { get; set; } = new List<TodoItem>();
}
