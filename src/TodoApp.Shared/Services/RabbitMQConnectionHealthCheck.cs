using Microsoft.Extensions.Diagnostics.HealthChecks;
using RabbitMQ.Client;

namespace TodoApp.Shared.Services;

/// <summary>
/// Reports healthy while the process-wide RabbitMQ connection is open. Both services publish
/// and consume over that single connection, so once it closes the process can no longer do
/// useful work and its orchestrator should restart it.
/// </summary>
public sealed class RabbitMQConnectionHealthCheck : IHealthCheck
{
    private readonly IConnection _connection;

    public RabbitMQConnectionHealthCheck(IConnection connection)
    {
        _connection = connection;
    }

    public Task<HealthCheckResult> CheckHealthAsync(
        HealthCheckContext context,
        CancellationToken cancellationToken = default
    ) =>
        Task.FromResult(
            _connection.IsOpen
                ? HealthCheckResult.Healthy()
                : HealthCheckResult.Unhealthy("RabbitMQ connection is closed")
        );
}
