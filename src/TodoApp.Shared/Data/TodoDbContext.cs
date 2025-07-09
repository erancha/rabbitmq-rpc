using Microsoft.EntityFrameworkCore;
using TodoApp.Shared.Models;

namespace TodoApp.Shared.Data;

public class TodoDbContext : DbContext
{
    // dotnet ef migrations add 'name' after making changes to the model
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
            entity.HasMany(e => e.Items)
                  .WithOne(e => e.User)
                  .HasForeignKey(e => e.UserId)
                  .OnDelete(DeleteBehavior.Cascade);
        });

        modelBuilder.Entity<TodoItem>(entity =>
        {
            entity.HasKey(e => e.Id);
            entity.Property(e => e.Title).IsRequired();
            entity.Property(e => e.Description).IsRequired(false);
            entity.Property(e => e.IsDeleted).HasDefaultValue(false);
        });

        base.OnModelCreating(modelBuilder);
    }
}
