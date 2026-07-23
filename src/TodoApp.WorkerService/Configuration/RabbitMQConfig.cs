namespace TodoApp.WorkerService.Configuration;

public static class RabbitMQConfig
{
    public const string UsersQueueName = "users-queue";

    public const string TodosQueueName = "todos-queue";

    public const string DeadLetterExchangeName = "todo-app-dead-letter-exchange";

    public const string DeadLetterQueueName = "dead-letter-queue";
}
