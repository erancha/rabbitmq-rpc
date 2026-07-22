using RabbitMQ.Client;

namespace TodoApp.Shared.Configuration.RabbitMQ;

public static class RoutingKeys
{
    public const string User = "user";
    public const string Todo = "todo";
}
