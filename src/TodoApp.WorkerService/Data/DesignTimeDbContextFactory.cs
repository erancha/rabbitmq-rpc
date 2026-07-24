using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Design;

namespace TodoApp.WorkerService.Data;

/// <summary>
/// Supplies a DbContext to the EF Core tools when generating migrations ('dotnet ef migrations
/// add'). The connection string below only tells those tools which provider to target — no
/// database is contacted, and at runtime migrations are applied through the DbContext registered
/// in Program.cs against the configured connection string.
/// </summary>
public class DesignTimeDbContextFactory : IDesignTimeDbContextFactory<TodoDbContext>
{
    public TodoDbContext CreateDbContext(string[] args)
    {
        var optionsBuilder = new DbContextOptionsBuilder<TodoDbContext>();
        optionsBuilder.UseNpgsql("Host=postgres;Database=tododb;Username=postgres;Password=postgres");

        return new TodoDbContext(optionsBuilder.Options);
    }
}
