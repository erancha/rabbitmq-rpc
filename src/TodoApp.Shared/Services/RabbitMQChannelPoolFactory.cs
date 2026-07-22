using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;

namespace TodoApp.Shared.Services;

/// <summary>
/// Creates the shared channel pool used by both services for RabbitMQ publishing.
/// </summary>
public static class RabbitMQChannelPoolFactory
{
    /// <summary>
    /// Creates an ObjectPool of channels over the given connection. Allocation is unbounded;
    /// the pool retains at most Environment.ProcessorCount * 2 channels for reuse (the
    /// DefaultObjectPoolProvider default) and excess returned channels are discarded.
    /// </summary>
    public static ObjectPool<IModel> CreateChannelPool(IConnection connection)
    {
        var policy = new ChannelPooledObjectPolicy(connection);
        var provider = new DefaultObjectPoolProvider();
        return provider.Create(policy);
    }
}
