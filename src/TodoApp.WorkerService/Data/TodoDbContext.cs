using Microsoft.EntityFrameworkCore;
using TodoApp.Shared.Models;

namespace TodoApp.WorkerService.Data;

// Schema of record for the Code-First model: every change here needs a matching migration
// ('dotnet ef migrations add <name>'), which the worker applies at startup.
public class TodoDbContext : DbContext
{
    public TodoDbContext(DbContextOptions<TodoDbContext> options) : base(options)
    {
    }

    public DbSet<User> Users { get; set; } = null!;
    public DbSet<TodoItem> TodoItems { get; set; } = null!;

    protected override void OnModelCreating(ModelBuilder modelBuilder)
    {
        modelBuilder.Entity<User>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Username).IsRequired();
            entity.HasIndex(e => e.Username).IsUnique();
            entity.HasIndex(e => e.Email).IsUnique();
            entity.Property(e => e.Email).IsRequired();
        });

        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Description).IsRequired(false);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
            // Deleting a user purges all their items, soft-deleted included — history does not outlive its owner.
            entity.HasOne<User>().WithMany().HasForeignKey(e => e.UserId).OnDelete(DeleteBehavior.Cascade);
        });

        base.OnModelCreating(modelBuilder);
    }
}
