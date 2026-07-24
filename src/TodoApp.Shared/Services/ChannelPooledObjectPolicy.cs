using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;

namespace TodoApp.Shared.Services;

/// <summary>
/// Pool policy for RabbitMQ channels over one shared connection. A channel the broker closed —
/// a channel-level protocol error settles that channel permanently — is discarded on return
/// rather than handed to the next borrower.
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
        return obj.IsOpen;
    }
}
