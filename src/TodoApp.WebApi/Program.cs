using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;
using TodoApp.Shared.Services;
using TodoApp.WebApi.Configuration;
using TodoApp.WebApi.Services;
using RabbitMQShared = TodoApp.Shared.Configuration.RabbitMQ;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
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

// Bind WebApiConfig to the WebApi section in appsettings.json
builder.Services.Configure<WebApiConfig>(builder.Configuration.GetSection("WebApi"));

// Build the application - this finalizes service registration and creates the root service provider.
// After this point, we can no longer register services, and the application can start resolving them.
var host = builder.Build();

// Configure the HTTP request pipeline.
if (host.Environment.IsDevelopment())
{
    host.UseSwagger();
    host.UseSwaggerUI();

    // Redirect root to Swagger UI
    host.MapGet("/", () => Results.Redirect("/swagger"));
}

host.UseHttpsRedirection();
host.UseAuthorization();
host.MapControllers();

// Register cleanup on application shutdown
host.Services.GetRequiredService<IHostApplicationLifetime>()
    .ApplicationStopping.Register(() => connection?.Close());

// DI Resolution happens in the following points:
//   1. When ASP.NET Core resolves services during startup,
//   2. When controllers are created for each request (resolving their dependencies),
//   3. When those services themselves resolve dependencies during runtime.
await host.RunAsync();
