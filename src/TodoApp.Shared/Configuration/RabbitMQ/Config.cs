using RabbitMQ.Client;

namespace TodoApp.Shared.Configuration.RabbitMQ;

public class Config
{
    public string Host { get; set; } = "localhost";
    public string Username { get; set; } = "guest";
    public string Password { get; set; } = "guest";
    public int Port { get; set; } = 5672;

    public const string AppExchangeName = "todo-app-exchange";
    public const string AppExchangeType = ExchangeType.Direct;
}
