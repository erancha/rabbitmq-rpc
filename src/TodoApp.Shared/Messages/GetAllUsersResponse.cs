using TodoApp.Shared.Models;

namespace TodoApp.Shared.Messages;

public class GetAllUsersResponse
{
    public List<User>? Users { get; set; }
}
