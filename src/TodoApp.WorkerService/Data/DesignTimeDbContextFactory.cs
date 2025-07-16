using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoApp.WorkerService.Data;

/// <summary>
/// Factory used by EF Core tools to create a DbContext when generating migrations with 'dotnet ef migrations add'.
/// At runtime, the application uses the DbContext registered in Program.cs to apply these migrations.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TodoDbContext>
{
    /// <summary>
    /// Creates a DbContext for generating migration files with 'dotnet ef migrations add'.
    /// The connection string here is only used during migration generation, not for applying migrations at runtime.
    /// </summary>
    public TodoDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TodoDbContext>();
        optionsBuilder.UseNpgsql("Host=postgres;Database=tododb;Username=postgres;Password=postgres");

        return new TodoDbContext(optionsBuilder.Options);
    }
}
