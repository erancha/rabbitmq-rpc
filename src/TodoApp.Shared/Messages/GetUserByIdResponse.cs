using TodoApp.Shared.Models;

namespace TodoApp.Shared.Messages;

public class GetUserByIdResponse
{
    public User? User { get; set; }
}
