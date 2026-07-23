using Xunit;
using Moq;
using RabbitMQ.Client;
using TodoApp.WorkerService.Configuration;
using TodoApp.WorkerService.Helpers;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the worker's queue topology: work queues dead-letter rejected messages into a
/// durable dead-letter queue instead of letting the broker destroy them.
/// </summary>
public class RabbitMQSetupTests
{
    private readonly Mock<IModel> _channel = new();

    public RabbitMQSetupTests()
    {
        RabbitMQSetup.DeclareAndBindQueues(_channel.Object);
    }

    [Theory]
    [InlineData(RabbitMQConfig.UsersQueueName)]
    [InlineData(RabbitMQConfig.TodosQueueName)]
    public void Work_queues_dead_letter_into_the_dead_letter_exchange(string queueName)
    {
        _channel.Verify(c => c.QueueDeclare(
            queueName, true, false, false,
            It.Is<IDictionary<string, object>>(args =>
                args != null
                && (string)args["x-dead-letter-exchange"] == RabbitMQConfig.DeadLetterExchangeName)),
            Times.Once);
    }

    [Fact]
    public void Dead_letter_exchange_is_declared_durable()
    {
        _channel.Verify(c => c.ExchangeDeclare(
            RabbitMQConfig.DeadLetterExchangeName, ExchangeType.Fanout, true, false,
            It.IsAny<IDictionary<string, object>>()), Times.Once);
    }

    [Fact]
    public void Dead_letter_queue_is_declared_durable_and_bound_to_the_dead_letter_exchange()
    {
        _channel.Verify(c => c.QueueDeclare(
            RabbitMQConfig.DeadLetterQueueName, true, false, false,
            It.IsAny<IDictionary<string, object>>()), Times.Once);
        _channel.Verify(c => c.QueueBind(
            RabbitMQConfig.DeadLetterQueueName, RabbitMQConfig.DeadLetterExchangeName,
            It.IsAny<string>(), It.IsAny<IDictionary<string, object>>()), Times.Once);
    }
}
