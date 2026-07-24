using Microsoft.Extensions.Diagnostics.HealthChecks;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// Exposes worker health to the container orchestrator without an HTTP endpoint: each healthy
/// report rewrites a heartbeat file (refreshing its modification time), and any other report
/// deletes it. The container healthcheck tests the file's existence and freshness, so both an
/// unhealthy report and a wedged process that stops publishing turn the container unhealthy.
/// </summary>
public sealed class HeartbeatFileHealthPublisher : IHealthCheckPublisher
{
    private readonly string _heartbeatFilePath;

    public HeartbeatFileHealthPublisher(string heartbeatFilePath)
    {
        _heartbeatFilePath = heartbeatFilePath;
    }

    public Task PublishAsync(HealthReport report, CancellationToken cancellationToken)
    {
        if (report.Status == HealthStatus.Healthy)
            File.WriteAllText(_heartbeatFilePath, string.Empty);
        else
            File.Delete(_heartbeatFilePath);

        return Task.CompletedTask;
    }
}
