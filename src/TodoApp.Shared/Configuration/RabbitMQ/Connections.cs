using RabbitMQ.Client;

namespace TodoApp.Shared.Configuration.RabbitMQ;

public static class Connections
{
    public static IConnection ConnectAndBindExchange(Config config,int maxRetries = 5) {
        var connectionFactory = new ConnectionFactory
        {
            HostName = config.Host,
            UserName = config.Username,
            Password = config.Password,
            Port = config.Port,
        };

        IConnection? connection = null;
        for (int retry = 1; retry <= maxRetries; retry++)
        {
            try
            {
                connection = connectionFactory.CreateConnection();
                Console.WriteLine($"Successfully connected to RabbitMQ on attempt {retry}");
                break;
            }
            catch (Exception)
            {
                if (retry == maxRetries)
                    throw;
                var delay = TimeSpan.FromSeconds(Math.Pow(2, retry - 1)); // 1, 2, 4, 8, 16 seconds
                Thread.Sleep(delay);
            }
        }

        if (connection is null)
            throw new InvalidOperationException("Failed to establish RabbitMQ connection");

        var channel = connection.CreateModel();

        // Declare exchange
        // Setting durable: true means the exchange will survive a RabbitMQ server restart
        // The exchange definition is persisted to disk and restored on server startup
        channel.ExchangeDeclare(
            exchange: Config.AppExchangeName,
            type: Config.AppExchangeType,
            durable: true
        );

        return connection;
    }
}
