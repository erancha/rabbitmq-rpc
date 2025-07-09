using RabbitMQ.Client;
using TodoApp.Shared.Configuration;
using TodoApp.WebApi.Services;

var builder = WebApplication.CreateBuilder(args);

// Add services to the container.
builder.Services.AddControllers();
builder.Services.AddEndpointsApiExplorer();
builder.Services.AddSwaggerGen();

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
        var delay = TimeSpan.FromSeconds(Math.Pow(2, retry - 1)); // 1, 2, 4, 8, 16 seconds
        Thread.Sleep(delay);
    }
}

if (connection is null)
    throw new InvalidOperationException("Failed to establish RabbitMQ connection");

var channel = connection.CreateModel();

// Register RabbitMQ services
builder.Services.AddSingleton<IModel>(channel);
builder.Services.AddSingleton<IRabbitMQMessageService, RabbitMQMessageService>();

// Declare exchange
// Setting durable: true means the exchange will survive a RabbitMQ server restart
// The exchange definition is persisted to disk and restored on server startup
channel.ExchangeDeclare(
    exchange: RabbitMQConfig.AppExchangeName,
    type: ExchangeType.Topic,
    durable: true
);

builder.Services.AddSingleton<IModel>(channel);

var app = builder.Build();

// Configure the HTTP request pipeline.
if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();

    // Redirect root to Swagger UI
    app.MapGet("/", () => Results.Redirect("/swagger"));
}

app.UseHttpsRedirection();
app.UseAuthorization();
app.MapControllers();

app.Run();

// Cleanup
AppDomain.CurrentDomain.ProcessExit += (s, e) =>
{
    channel?.Close();
    connection?.Close();
};
