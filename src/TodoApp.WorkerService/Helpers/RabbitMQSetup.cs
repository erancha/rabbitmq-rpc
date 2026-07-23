using RabbitMQ.Client;
using TodoApp.WorkerService.Configuration;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

namespace TodoApp.WorkerService.Helpers;

public static class RabbitMQSetup
{
    /// <summary>
    /// Declares the durable users/todos queues bound to the app exchange, and the dead-letter
    /// exchange/queue pair that retains messages the handlers reject.
    /// </summary>
    public static void DeclareAndBindQueues(IModel channel)
    {
        // Rejected messages (BasicNack, requeue: false) route here instead of being destroyed,
        // so failed requests stay available for inspection and replay. Fanout: the dead-letter
        // queue must capture messages regardless of their original routing key.
        channel.ExchangeDeclare(
            exchange: RabbitMQConfig.DeadLetterExchangeName,
            type: ExchangeType.Fanout,
            durable: true
        );

        channel.QueueDeclare(
            queue: RabbitMQConfig.DeadLetterQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false
        );

        channel.QueueBind(
            queue: RabbitMQConfig.DeadLetterQueueName,
            exchange: RabbitMQConfig.DeadLetterExchangeName,
            routingKey: string.Empty
        );

        var deadLetterArguments = new Dictionary<string, object>
        {
            { "x-dead-letter-exchange", RabbitMQConfig.DeadLetterExchangeName },
        };

        channel.QueueDeclare(
            queue: RabbitMQConfig.UsersQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: deadLetterArguments
        );

        channel.QueueDeclare(
            queue: RabbitMQConfig.TodosQueueName,
            durable: true,
            exclusive: false,
            autoDelete: false,
            arguments: deadLetterArguments
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
