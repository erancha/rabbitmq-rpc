using Microsoft.EntityFrameworkCore;
using TodoApp.Shared.Data;

namespace TodoApp.WorkerService.Services;

/// <summary>
/// A hosted service responsible for initializing the database.
/// Ensures database is ready by running any pending migrations before the application starts.
/// </summary>
public class DatabaseInitializationService : IHostedService
{
    private readonly IServiceProvider _serviceProvider;
    private readonly ILogger<DatabaseInitializationService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="DatabaseInitializationService"/> class.
    /// </summary>
    /// <param name="serviceProvider">The service provider for dependency resolution.</param>
    /// <param name="logger">Logger for diagnostic information.</param>
    public DatabaseInitializationService(
        IServiceProvider serviceProvider,
        ILogger<DatabaseInitializationService> logger
    )
    {
        _serviceProvider = serviceProvider;
        _logger = logger;
    }

    /// <summary>
    /// Called when the service starts. Performs database migrations and initializes message handlers.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the startup process.</param>
    /// <returns>A task representing the startup operation.</returns>
    public async Task StartAsync(CancellationToken cancellationToken)
    {
        try
        {
            using var scope = _serviceProvider.CreateScope();

            // First, ensure database is migrated
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
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Error during startup: {Message}", ex.Message);
            throw;
        }
    }

    /// <summary>
    /// Called when the service stops. Currently performs no cleanup as the message handlers are scoped.
    /// </summary>
    /// <param name="cancellationToken">A token that can be used to cancel the shutdown process.</param>
    /// <returns>A completed task since no async cleanup is required.</returns>
    public Task StopAsync(CancellationToken cancellationToken)
    {
        return Task.CompletedTask;
    }
}
