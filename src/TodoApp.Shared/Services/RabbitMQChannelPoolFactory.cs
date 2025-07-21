using Microsoft.Extensions.ObjectPool;
using RabbitMQ.Client;

namespace TodoApp.Shared.Services;

/// <summary>
/// Factory for creating RabbitMQ channel pools.
/// Provides reusable methods for setting up channel pooling across different services.
/// </summary>
public static class RabbitMQChannelPoolFactory
{
    /// <summary>
    /// Creates an ObjectPool<IModel> for RabbitMQ channels.
    /// 
    /// Pool Configuration:
    /// - Maximum retained channels: Environment.ProcessorCount * 2 (default from DefaultObjectPoolProvider)
    /// - No limit on total allocation - pool only limits retained objects, excess are GC'd
    /// - The IConnection passed here is injected into ChannelPooledObjectPolicy constructor
    /// - Pool can allocate unlimited channels but only retains up to MaximumRetained for reuse
    /// </summary>
    /// <param name="connection">The RabbitMQ connection to create channels from</param>
    /// <returns>A configured ObjectPool for RabbitMQ channels</returns>
    public static ObjectPool<IModel> CreateChannelPool(IConnection connection)
    {
        var policy = new ChannelPooledObjectPolicy(connection);
        var provider = new DefaultObjectPoolProvider();
        return provider.Create(policy);
    }
}
