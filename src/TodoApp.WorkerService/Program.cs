using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;
using TodoApp.WorkerService.Configuration;
using TodoApp.WorkerService.Data;
using TodoApp.WorkerService.Services;
using TodoApp.WorkerService.Helpers;
using TodoApp.Shared.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);

// Configure RabbitMQ
builder.Services.Configure<RabbitMQShared.Config>(builder.Configuration.GetSection("RabbitMQ"));
var rabbitMQConfig =
    builder.Configuration.GetSection("RabbitMQ").Get<RabbitMQShared.Config>()
    ?? new RabbitMQShared.Config();
IConnection connection = RabbitMQShared.Connections.ConnectAndBindExchange(rabbitMQConfig);
var channelPool = RabbitMQChannelPoolFactory.CreateChannelPool(connection);
builder.Services.AddSingleton<ObjectPool<IModel>>(_ => channelPool);

// Setup queues using a channel from the pool
var setupChannel = channelPool.Get();
RabbitMQSetup.DeclareAndBindQueues(setupChannel);
channelPool.Return(setupChannel);

// Configure Database: Register a factory to create DbContext instances with a scoped lifetime:
builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Register initialization service to run migrations before the application starts and an initialization signal
// on which the message handlers (below) will wait before starting to consume requests, to make sure the database is ready:
builder.Services.AddSingleton<DbInitializationSignal>();
builder.Services.AddHostedService<DbInitializationService>();

// Register message handlers as hosted services (automatically creates singleton instances and manages their lifecycle)
// Multiple instances for increased throughput - using wrapper classes since AddHostedService<T> creates singletons
builder.Services.AddHostedService<UserMessageHandler>();
#pragma warning disable CS1591 // Missing XML comment for publicly visible type or member
#region UserMessageHandler Registrations
// TODO: This wrappers is a temp workaround to force additional instance - research further!
// builder.Services.AddHostedService<UserMessageHandler2>();
#endregion
#pragma warning restore CS1591
builder.Services.AddHostedService<TodoItemMessageHandler>();

// Build the application - this finalizes service registration and creates the root service provider.
// After this point, we can no longer register services, and the application can start resolving them.
var host = builder.Build();

// Register cleanup on application shutdown
host.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStopping.Register(() => connection?.Close());

// DI Resolution happens in the following points:
//   1. When ASP.NET Core resolves services during startup,
//   2. When controllers are created for each request (resolving their dependencies),
//   3. When those services themselves resolve dependencies during runtime.
await host.RunAsync();

// The host:
//   1. Starts all registered hosted services by calling their StartAsync methods in order
//   2. Keeps the application running
//   3. Handles application shutdown (calls StopAsync on all hosted services)
