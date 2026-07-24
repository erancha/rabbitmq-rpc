using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;
using TodoApp.WorkerService.Configuration;
using TodoApp.WorkerService.Data;
using TodoApp.WorkerService.Services;
using TodoApp.WorkerService.Helpers;
using TodoApp.Shared.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

var builder = Host.CreateApplicationBuilder(args);

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

builder.Services.AddHealthChecks()
    .AddCheck("rabbitmq", new RabbitMQConnectionHealthCheck(connection));

// Heartbeat publishing is opt-in via HEARTBEAT_FILE: the container orchestrator sets it and
// probes the file's freshness; local runs have no probe, so no file is written.
var heartbeatFilePath = builder.Configuration["HEARTBEAT_FILE"];
if (heartbeatFilePath != null)
    builder.Services.AddSingleton<IHealthCheckPublisher>(
        new HeartbeatFileHealthPublisher(heartbeatFilePath)
    );

builder.Services.AddDbContext<TodoDbContext>(options =>
    options.UseNpgsql(builder.Configuration.GetConnectionString("DefaultConnection"))
);

// The signal is what orders startup: migrations run to completion before any handler consumes,
// so no message is processed against an unmigrated database.
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
