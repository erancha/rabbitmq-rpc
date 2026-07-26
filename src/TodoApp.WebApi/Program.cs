using Microsoft.Extensions.ObjectPool;
using Microsoft.Extensions.Options;
using RabbitMQ.Client;
using TodoApp.Shared.Services;
using TodoApp.WebApi.Configuration;
using TodoApp.WebApi.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

builder.Services.Configure<RabbitMQShared.Config>(builder.Configuration.GetSection("RabbitMQ"));
var rabbitMQConfig =
    builder.Configuration.GetSection("RabbitMQ").Get<RabbitMQShared.Config>()
    ?? new RabbitMQShared.Config();
// Publish and reply traffic ride separate TCP connections: on a shared socket every frame
// serializes through one connection, so reply deliveries queue behind bursts of publish frames.
IConnection publishConnection = RabbitMQShared.Connections.ConnectAndBindExchange(rabbitMQConfig);
IConnection consumeConnection = RabbitMQShared.Connections.ConnectAndBindExchange(rabbitMQConfig);
builder.Services.AddSingleton<ObjectPool<IModel>>(_ => RabbitMQChannelPoolFactory.CreateChannelPool(publishConnection));
builder.Services.AddSingleton<IRabbitMQMessageService>(sp => new RabbitMQMessageService(
    sp.GetRequiredService<ObjectPool<IModel>>(),
    consumeConnection.CreateModel(),
    sp.GetRequiredService<ILogger<RabbitMQMessageService>>(),
    sp.GetRequiredService<IOptions<WebApiConfig>>()));
builder.Services.AddHealthChecks()
    .AddCheck("rabbitmq-publish", new RabbitMQConnectionHealthCheck(publishConnection))
    .AddCheck("rabbitmq-consume", new RabbitMQConnectionHealthCheck(consumeConnection));

builder.Services.Configure<WebApiConfig>(builder.Configuration.GetSection("WebApi"));

var host = builder.Build();

if (host.Environment.IsDevelopment())
{
    host.UseSwagger();
    host.UseSwaggerUI();

    host.MapGet("/", () => Results.Redirect("/swagger"));
}

host.MapControllers();
host.MapHealthChecks("/health");

host.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStopping.Register(() =>
    {
        publishConnection.Close();
        consumeConnection.Close();
    });

await host.RunAsync();
