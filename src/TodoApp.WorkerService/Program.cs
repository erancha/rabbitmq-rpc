using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using TodoApp.Shared.Data;
using TodoApp.WorkerService.Configuration;
using TodoApp.WorkerService.Services;
using TodoApp.WorkerService.Helpers;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);

// Configure Database
builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Configure RabbitMQ
var rabbitMQConfig =
    builder.Configuration.GetSection("RabbitMQ").Get<RabbitMQShared.Config>()
    ?? new RabbitMQShared.Config();

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

// Declare exchange
channel.ExchangeDeclare(
    exchange: RabbitMQShared.Config.AppExchangeName,
    type: RabbitMQShared.Config.AppExchangeType,
    durable: true
);

// QueueDeclare options:
// - durable: queue and messages survive broker restarts (default=false)
// - autoDelete: delete queue when last consumer disconnects (default=false)
// - exclusive: allow multiple connections (default=false)
// - arguments: optional settings like TTL, max length (default=null)

// Declare queues
channel.QueueDeclare(
    queue: RabbitMQConfig.UsersQueueName,
    durable: true // queue and messages survive broker restarts
);

channel.QueueDeclare(
    queue: RabbitMQConfig.TodosQueueName,
    durable: true // queue and messages survive broker restarts
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

builder.Services.AddSingleton<IModel>(channel);

// Register message handlers as hosted services
// This automatically creates singleton instances and manages their lifecycle
builder.Services.AddHostedService<UserMessageHandler>();
builder.Services.AddHostedService<TodoItemMessageHandler>();

// Register database initialization service to run migrations before the application starts
builder.Services.AddHostedService<DatabaseInitializationService>();
var host = builder.Build();

// Set up logger for RpcResponseHelper
var loggerFactory = host.Services.GetRequiredService<ILoggerFactory>();
RpcResponseHelper.SetLogger(loggerFactory.CreateLogger(nameof(RpcResponseHelper)));

await host.RunAsync();
// The host:
// 1. Starts all registered hosted services by calling their StartAsync methods in order
// 2. Keeps the application running
// 3. Handles application shutdown (calls StopAsync on all hosted services)
