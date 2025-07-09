using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using TodoApp.Shared.Configuration;
using TodoApp.Shared.Data;
using TodoApp.WorkerService.Configuration;
using TodoApp.WorkerService.Services;

var builder = Host.CreateApplicationBuilder(args);

// Configure Database
builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Configure RabbitMQ
var rabbitMQConfig =
    builder.Configuration.GetSection("RabbitMQ").Get<RabbitMQConfig>() ?? new RabbitMQConfig();

var factory = new ConnectionFactory
{
    HostName = rabbitMQConfig.Host,
    UserName = rabbitMQConfig.Username,
    Password = rabbitMQConfig.Password,
    Port = rabbitMQConfig.Port,
};

IConnection? connection = null;
for (int retry = 1; retry <= 5; retry++)
{
    try
    {
        connection = factory.CreateConnection();
        Console.WriteLine($"Successfully connected to RabbitMQ on attempt {retry}");
        break;
    }
    catch (Exception)
    {
        if (retry == 5)
            throw;
        var delay = TimeSpan.FromSeconds(Math.Pow(2, retry - 1));
        Thread.Sleep(delay);
    }
}

if (connection is null)
    throw new InvalidOperationException("Failed to establish RabbitMQ connection");

var channel = connection.CreateModel();

// Declare exchange and queue
channel.ExchangeDeclare(
    exchange: RabbitMQConfig.AppExchangeName,
    type: ExchangeType.Topic,
    durable: true
);

// Declare queues
channel.QueueDeclare(
    queue: QueueConfiguration.UsersQueueName,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);

channel.QueueDeclare(
    queue: QueueConfiguration.TodosQueueName,
    durable: true,
    exclusive: false,
    autoDelete: false,
    arguments: null
);

// Bind queues to exchange with routing keys
channel.QueueBind(
    queue: QueueConfiguration.UsersQueueName,
    exchange: RabbitMQConfig.AppExchangeName,
    routingKey: QueueConfiguration.RoutingKeys.UserEvents
);

channel.QueueBind(
    queue: QueueConfiguration.TodosQueueName,
    exchange: RabbitMQConfig.AppExchangeName,
    routingKey: QueueConfiguration.RoutingKeys.TodoEvents
);

builder.Services.AddSingleton<IModel>(channel);

// Message handlers are registered as singletons since they are long-lived services that manage RabbitMQ consumers.
// They use IServiceScopeFactory internally to create scopes for each message processing operation.
builder.Services.AddSingleton<UserMessageHandler>();
builder.Services.AddSingleton<TodoItemMessageHandler>();

// Register the MessageProcessingService as a hosted service (a background task/service that is managed by the .NET application host (IHost)).
// This ensures proper initialization sequence:
// 1. Application starts
// 2. MessageProcessingService.StartAsync is called
// 3. Database migrations are applied
// 4. Message handlers begin processing RabbitMQ messages
builder.Services.AddHostedService<MessageProcessingService>();
var host = builder.Build();
await host.RunAsync();
// The host:
// 1. Starts all registered hosted services by calling their StartAsync methods in order
// 2. Keeps the application running
// 3. Handles application shutdown (calls StopAsync on all hosted services)
