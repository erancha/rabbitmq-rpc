namespace TodoApp.WorkerService.Configuration;

public static class QueueConfiguration
{
    public const string UsersQueueName = "users-queue";
    public const string TodosQueueName = "todos-queue";
    
    public static class RoutingKeys
    {
        public const string UserEvents = "user.*";
        public const string TodoEvents = "todo.*";
    }
}
