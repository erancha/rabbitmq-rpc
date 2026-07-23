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

        // Short-lived channel used only to declare the exchange.
        using (var channel = connection.CreateModel())
        {
            channel.ExchangeDeclare(
                exchange: Config.AppExchangeName,
                type: Config.AppExchangeType,
                durable: true
            );
        }

        return connection;
    }
}
