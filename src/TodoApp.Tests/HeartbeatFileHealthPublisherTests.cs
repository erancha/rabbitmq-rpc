using Xunit;
using Microsoft.Extensions.Diagnostics.HealthChecks;
using TodoApp.WorkerService.Services;

namespace TodoApp.Tests;

/// <summary>
/// Verifies the worker's file-based health signal: healthy reports keep the heartbeat file
/// fresh (the container healthcheck tests its modification time), and any other report
/// removes it so the container turns unhealthy immediately instead of after the staleness
/// window.
/// </summary>
public class HeartbeatFileHealthPublisherTests : IDisposable
{
    private readonly string _heartbeatFilePath =
        Path.Combine(Path.GetTempPath(), $"heartbeat-test-{Guid.NewGuid():N}");

    public void Dispose() => File.Delete(_heartbeatFilePath);

    private static HealthReport Report(HealthStatus status) =>
        new(new Dictionary<string, HealthReportEntry>(), status, TimeSpan.Zero);

    [Fact]
    public async Task HealthyReportCreatesHeartbeatFile()
    {
        var publisher = new HeartbeatFileHealthPublisher(_heartbeatFilePath);

        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);

        Assert.True(File.Exists(_heartbeatFilePath));
    }

    [Fact]
    public async Task HealthyReportRefreshesHeartbeatTimestamp()
    {
        var publisher = new HeartbeatFileHealthPublisher(_heartbeatFilePath);
        File.WriteAllText(_heartbeatFilePath, string.Empty);
        var staleTime = DateTime.UtcNow.AddHours(-1);
        File.SetLastWriteTimeUtc(_heartbeatFilePath, staleTime);

        await publisher.PublishAsync(Report(HealthStatus.Healthy), CancellationToken.None);

        Assert.True(File.GetLastWriteTimeUtc(_heartbeatFilePath) > staleTime.AddMinutes(30));
    }

    [Fact]
    public async Task UnhealthyReportRemovesHeartbeatFile()
    {
        var publisher = new HeartbeatFileHealthPublisher(_heartbeatFilePath);
        File.WriteAllText(_heartbeatFilePath, string.Empty);

        await publisher.PublishAsync(Report(HealthStatus.Unhealthy), CancellationToken.None);

        Assert.False(File.Exists(_heartbeatFilePath));
    }
}
