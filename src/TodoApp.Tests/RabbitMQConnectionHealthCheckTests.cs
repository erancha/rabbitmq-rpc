using Xunit;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using Moq;
using RabbitMQ.Client;
using TodoApp.Shared.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the broker readiness check both services expose to their orchestrator:
/// healthy exactly while the process-wide RabbitMQ connection is open.
/// </summary>
public class RabbitMQConnectionHealthCheckTests
{
    private static Task<HealthCheckResult> CheckAsync(bool isOpen)
    {
        var connection = new Mock<IConnection>();
        connection.Setup(c => c.IsOpen).Returns(isOpen);
        var healthCheck = new RabbitMQConnectionHealthCheck(connection.Object);
        return healthCheck.CheckHealthAsync(new HealthCheckContext(), CancellationToken.None);
    }

    [Fact]
    public async Task ReportsHealthyWhileConnectionIsOpen()
    {
        var result = await CheckAsync(isOpen: true);

        Assert.Equal(HealthStatus.Healthy, result.Status);
    }

    [Fact]
    public async Task ReportsUnhealthyWhenConnectionIsClosed()
    {
        var result = await CheckAsync(isOpen: false);

        Assert.Equal(HealthStatus.Unhealthy, result.Status);
    }
}
