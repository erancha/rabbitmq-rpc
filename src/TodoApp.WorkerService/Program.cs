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

var setupChannel = channelPool.Get();
RabbitMQSetup.DeclareAndBindQueues(setupChannel);
channelPool.Return(setupChannel);

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// Register initialization service to run migrations before the application starts and an initialization signal
// on which the message handlers (below) will wait before starting to consume requests, to make sure the database is ready:
builder.Services.AddSingleton<DbInitializationSignal>();
builder.Services.AddHostedService<DbInitializationService>();

// Register one consumer per queue. Throughput scales horizontally by running more worker
// replicas (compose: services.worker.deploy.replicas), which compete on the same durable queues.
builder.Services.AddHostedService<UserMessageHandler>();
builder.Services.AddHostedService<TodoItemMessageHandler>();

var host = builder.Build();

host.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStopping.Register(() => connection?.Close());

await host.RunAsync();
