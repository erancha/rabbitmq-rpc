using RabbitMQ.Client;

namespace TodoApp.Shared.Configuration.RabbitMQ;

public static class RoutingKeys
{
    // Direct routing keys for each entity type
    public const string User = "user";
    public const string Todo = "todo";
}
