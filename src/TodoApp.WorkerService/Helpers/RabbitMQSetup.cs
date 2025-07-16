using RabbitMQ.Client;
using TodoApp.WorkerService.Configuration;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.WorkerService.Helpers;

public static class RabbitMQSetup
{
    /// <summary>
    /// Declares and binds the required queues for the worker service.
    /// </summary>
    /// <param name="channel">The RabbitMQ channel to use</param>
    /// <remarks>
    /// Queue options:
    /// - durable: queue and messages survive broker restarts (default=false)
    /// - autoDelete: delete queue when last consumer disconnects (default=false)
    /// - exclusive: only allow access from the declaring connection (default: true)
    /// - arguments: optional settings like TTL, max length (default=null)
    /// </remarks>
    public static void DeclareAndBindQueues(IModel channel)
    {
        // Declare queues
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

        // Bind queues with direct routing keys
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
