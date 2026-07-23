using Microsoft.EntityFrameworkCore;
using TodoApp.WorkerService.Data;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// Hosted service that probes database reachability with exponential backoff — failing startup
/// if the database never becomes reachable — then runs pending EF migrations and marks
/// DbInitializationSignal complete so the message handlers can start consuming.
/// </summary>
public class DbInitializationService : IHostedService
{
    private const int MaxConnectionAttempts = 5;

    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DbInitializationService> _logger;
    private readonly DbInitializationSignal _dbInitializationSignal;

    public DbInitializationService(
        IServiceProvider serviceProvider,
        ILogger<DbInitializationService> logger,
        DbInitializationSignal dbInitializationSignal)
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
        _dbInitializationSignal = dbInitializationSignal;
    }

    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();
            var dbContext = scope.ServiceProvider.GetRequiredService<TodoDbContext>();

            _logger.LogInformation("Checking database connection...");
            var connected = false;
            for (int retry = 1; retry <= MaxConnectionAttempts && !connected; retry++)
            {
                // An exception on the final attempt propagates as-is; a false result on the
                // final attempt is turned into the failure below.
                Exception? probeFailure = null;
                try
                {
                    connected = await dbContext.Database.CanConnectAsync(cancellationToken);
                }
                catch (Exception ex) when (retry < MaxConnectionAttempts)
                {
                    probeFailure = ex;
                }

                if (!connected && retry < MaxConnectionAttempts)
                {
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retry - 1)); // 1, 2, 4, 8 seconds
                    _logger.LogWarning(
                        probeFailure,
                        "Database not reachable on attempt {Retry}/{MaxAttempts}. Retrying in {DelaySeconds} seconds...",
                        retry, MaxConnectionAttempts, delay.TotalSeconds
                    );
                    await DelayAsync(delay, cancellationToken);
                }
            }

            if (!connected)
                throw new InvalidOperationException(
                    $"Database is unreachable after {MaxConnectionAttempts} connection attempts"
                );

            _logger.LogInformation("Database connection successful");

            _logger.LogInformation("Starting database migration...");
            var pendingMigrations = await dbContext.Database.GetPendingMigrationsAsync(
                cancellationToken
            );
            _logger.LogInformation($"Found {pendingMigrations.Count()} pending migrations");

            try
            {
                await dbContext.Database.MigrateAsync(cancellationToken);

                // Verify tables were created
                var tables = await dbContext
                    .Database.SqlQuery<string>(
                        $"SELECT table_name FROM information_schema.tables WHERE table_schema = 'public' AND table_type = 'BASE TABLE'"
                    )
                    .ToListAsync(cancellationToken);

                _logger.LogInformation($"Tables in database: {string.Join(", ", tables)}");
            }
            catch (Exception ex)
            {
                _logger.LogError(ex, "Error during migration: {Message}", ex.Message);
                throw;
            }

            _logger.LogInformation("Database migrations completed");
            _logger.LogInformation("Database initialization completed");
            _dbInitializationSignal.MarkAsComplete();
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during startup: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Waits between connection attempts. Overridable so tests can assert the backoff schedule
    /// without real waiting.
    /// </summary>
    protected virtual Task DelayAsync(TimeSpan delay, CancellationToken cancellationToken) =>
        Task.Delay(delay, cancellationToken);

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
