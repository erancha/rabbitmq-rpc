using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Options;
using TodoApp.WorkerService.Configuration;
using TodoApp.WorkerService.Data;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// Deletes expired idempotency markers on a fixed interval so the ProcessedMessages table stays
/// bounded. A marker is useful only during the redelivery/retry window; past the configured
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
            "Idempotency marker sweep started: deleting markers older than {Retention}, every {SweepInterval}",
            _options.Retention, _options.SweepInterval);

        while (!stoppingToken.IsCancellationRequested)
        {
            await SweepOnce(stoppingToken);

            try
            {
                await Task.Delay(_options.SweepInterval, stoppingToken);
            }
            catch (OperationCanceledException)
            {
                break;
            }
        }
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
