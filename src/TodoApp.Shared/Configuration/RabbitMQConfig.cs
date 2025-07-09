namespace TodoApp.Shared.Configuration;

public class RabbitMQConfig
{
    public string Host { get; set; } = "localhost";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public int Port { get; set; } = 5672;

    public const string AppExchangeName = "todo-app-exchange";
}
