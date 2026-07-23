using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;
using TodoApp.Shared.Services;
using TodoApp.WebApi.Configuration;
using TodoApp.WebApi.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

// Configure RabbitMQ
builder.Services.Configure<RabbitMQShared.Config>(builder.Configuration.GetSection("RabbitMQ"));
var rabbitMQConfig =
    builder.Configuration.GetSection("RabbitMQ").Get<RabbitMQShared.Config>()
    ?? new RabbitMQShared.Config();
IConnection connection = RabbitMQShared.Connections.ConnectAndBindExchange(rabbitMQConfig);
builder.Services.AddSingleton<ObjectPool<IModel>>(_ => RabbitMQChannelPoolFactory.CreateChannelPool(connection));
builder.Services.AddSingleton<IRabbitMQMessageService, RabbitMQMessageService>();

builder.Services.Configure<WebApiConfig>(builder.Configuration.GetSection("WebApi"));

var host = builder.Build();

// Configure the HTTP request pipeline.
if (host.Environment.IsDevelopment())
{
    host.UseSwagger();
    host.UseSwaggerUI();

    host.MapGet("/", () => Results.Redirect("/swagger"));
}

host.MapControllers();

host.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStopping.Register(() => connection?.Close());

await host.RunAsync();
