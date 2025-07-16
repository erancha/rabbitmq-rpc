using Microsoft.EntityFrameworkCore;
using RabbitMQ.Client;
using TodoApp.Shared.Helpers;
using TodoApp.WorkerService.Configuration;
using TodoApp.WorkerService.Data;
using TodoApp.WorkerService.Helpers;
using TodoApp.WorkerService.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);

// Configure RabbitMQ
builder.Services.Configure<RabbitMQShared.Config>(builder.Configuration.GetSection("RabbitMQ"));
var rabbitMQConfig =
    builder.Configuration.GetSection("RabbitMQ").Get<RabbitMQShared.Config>()
    ?? new RabbitMQShared.Config();

(IConnection connection, IModel channel) =
    TodoApp.Shared.Helpers.RabbitMQConnections.CreateConnection(rabbitMQConfig);

builder.Services.AddSingleton<IModel>(channel);
RabbitMQSetup.DeclareAndBindQueues(channel);

// Configure Database: Register a factory to create DbContext instances with a scoped lifetime:
builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Register initialization service to run migrations before the application starts and an initialization signal
// on which the message handlers (below) will wait before starting to consume requests, to make sure the database is ready:
builder.Services.AddSingleton<InitializationSignal>();
builder.Services.AddHostedService<DatabaseInitializationService>();

// Register message handlers as hosted services (automatically creates singleton instances and manages their lifecycle)
builder.Services.AddHostedService<UserMessageHandler>();
builder.Services.AddHostedService<TodoItemMessageHandler>();

// Build the application - this finalizes service registration and creates the root service provider.
// After this point, we can no longer register services, and the application can start resolving them.
var host = builder.Build();

// Register cleanup on application shutdown
host.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStopping.Register(() =>
    {
        channel?.Close();
        connection?.Close();
    });

// DI Resolution happens in the following points:
//   1. When ASP.NET Core resolves services during startup,
//   2. When controllers are created for each request (resolving their dependencies),
//   3. When those services themselves resolve dependencies during runtime.
await host.RunAsync();

// The host:
//   1. Starts all registered hosted services by calling their StartAsync methods in order
//   2. Keeps the application running
//   3. Handles application shutdown (calls StopAsync on all hosted services)
