using RabbitMQ.Client;
using TodoApp.WorkerService.Configuration;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.WorkerService.Helpers;

public static class RabbitMQSetup
{
    /// <summary>
    /// Declares the durable users/todos queues and binds them to the app exchange.
    /// </summary>
    public static void DeclareAndBindQueues(IModel channel)
    {
        channel.QueueDeclare(
            queue: RabbitMQConfig.UsersQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false
        );

        channel.QueueDeclare(
            queue: RabbitMQConfig.TodosQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false
        );

        channel.QueueBind(
            queue: RabbitMQConfig.UsersQueueName,
            exchange: RabbitMQShared.Config.AppExchangeName,
            routingKey: RabbitMQShared.RoutingKeys.User
        );

        channel.QueueBind(
            queue: RabbitMQConfig.TodosQueueName,
            exchange: RabbitMQShared.Config.AppExchangeName,
            routingKey: RabbitMQShared.RoutingKeys.Todo
        );
    }
}
