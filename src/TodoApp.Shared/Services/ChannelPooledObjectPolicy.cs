using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;

namespace TodoApp.Shared.Services;

/// <summary>
/// Object pool policy for managing RabbitMQ channel lifecycle.
/// Handles creation and return of channels to the pool.
/// </summary>
public class ChannelPooledObjectPolicy : IPooledObjectPolicy<IModel>
{
    private readonly IConnection _connection;

    public ChannelPooledObjectPolicy(IConnection connection)
    {
        _connection = connection;
    }

    public IModel Create()
    {
        return _connection.CreateModel();
    }

    public bool Return(IModel obj)
    {
        // Only return healthy channels to the pool
        return obj?.IsOpen == true;
    }
}
