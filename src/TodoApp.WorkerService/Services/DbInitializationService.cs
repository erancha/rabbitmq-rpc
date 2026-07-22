using Microsoft.EntityFrameworkCore;
using TodoApp.WorkerService.Data;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// Hosted service that connects to the database with retries, runs pending EF migrations, and
/// then marks DbInitializationSignal complete so the message handlers can start consuming.
/// </summary>
public class DbInitializationService : IHostedService
{
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
            for (int retry = 1; retry <= 5; retry++)
            {
                try
                {
                    if (await dbContext.Database.CanConnectAsync(cancellationToken))
                    {
                        _logger.LogInformation(
                            $"Database connection successful on attempt {retry}"
                        );
                        break;
                    }
                }
                catch (Exception ex)
                {
                    if (retry == 5)
                        throw;
                    var delay = TimeSpan.FromSeconds(Math.Pow(2, retry - 1)); // 1, 2, 4, 8, 16 seconds
                    _logger.LogWarning(
                        ex,
                        $"Failed to connect to database on attempt {retry}/5. Retrying in {delay.TotalSeconds} seconds..."
                    );
                    await Task.Delay(delay, cancellationToken);
                }
            }

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

    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
