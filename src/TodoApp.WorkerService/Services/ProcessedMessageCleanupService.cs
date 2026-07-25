using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TodoApp.WorkerService.Configuration;
using TodoApp.WorkerService.Data;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// Deletes expired idempotency markers once per day, at a configured off-peak UTC hour, so the
/// ProcessedMessages table stays bounded without the bulk delete competing with peak request
/// traffic. A marker is useful only during the redelivery/retry window; past the configured
/// retention age it is dead weight. Waits for database initialization before its first sweep.
/// </summary>
public class ProcessedMessageCleanupService : BackgroundService
{
    private readonly IServiceScopeFactory _scopeFactory;
    private readonly ILogger<ProcessedMessageCleanupService> _logger;
    private readonly DbInitializationSignal _dbInitializationSignal;
    private readonly IdempotencyOptions _options;

    public ProcessedMessageCleanupService(
        IServiceScopeFactory scopeFactory,
        ILogger<ProcessedMessageCleanupService> logger,
        DbInitializationSignal dbInitializationSignal,
        IOptions<IdempotencyOptions> options)
    {
        _scopeFactory = scopeFactory;
        _logger = logger;
        _dbInitializationSignal = dbInitializationSignal;
        _options = options.Value;
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        await _dbInitializationSignal.Initialization;

        _logger.LogInformation(
            "Idempotency marker sweep started: deleting markers older than {Retention}, daily at {DailySweepAtUtc} UTC",
            _options.Retention, _options.DailySweepAtUtc);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepOnce(stoppingToken);

            try
            {
                await Task.Delay(DelayUntilNextSweep(DateTime.UtcNow, _options.DailySweepAtUtc), stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
    }

    /// <summary>
    /// Computes how long to wait from <paramref name="utcNow"/> until the next occurrence of the
    /// daily sweep time. Returns a full day when the target hour has already passed today (or is
    /// exactly now), so the sweep fires once per day at the configured UTC hour.
    /// </summary>
    public static TimeSpan DelayUntilNextSweep(DateTime utcNow, TimeSpan dailySweepAtUtc)
    {
        var todaysSweep = utcNow.Date + dailySweepAtUtc;
        var nextSweep = todaysSweep > utcNow ? todaysSweep : todaysSweep.AddDays(1);
        return nextSweep - utcNow;
    }

    private async Task SweepOnce(CancellationToken stoppingToken)
    {
        try
        {
            var cutoff = DateTime.UtcNow - _options.Retention;
            using var scope = _scopeFactory.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

            var deleted = await dbContext.ProcessedMessages
                .Where(m => m.CreatedAt < cutoff)
                .ExecuteDeleteAsync(stoppingToken);

            if (deleted > 0)
                _logger.LogInformation(
                    "Swept {Count} idempotency markers older than {Retention}", deleted, _options.Retention);
        }
        catch (OperationCanceledException) when (stoppingToken.IsCancellationRequested)
        {
            // Shutdown, not a failure.
        }
        catch (Exception ex)
        {
            // A failed sweep is not fatal: the markers persist and are retried next interval, and
            // correctness never depends on them being gone — only the table's size does.
            _logger.LogError(ex, "Idempotency marker sweep failed; retrying next interval");
        }
    }
}
