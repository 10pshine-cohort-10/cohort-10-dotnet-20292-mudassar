using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.Identity.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.ChangeTracking;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Data;

public class AppDbContext : IdentityDbContext<ApplicationUser, IdentityRole<Guid>, Guid>
{
    public AppDbContext(DbContextOptions<AppDbContext> options) : base(options)
    {
        // Defer cascade delete evaluation until SaveChanges so ConvertTaskDeletesToSoftDelete
        // can intercept TaskItem deletions before EF Core marks loaded comments/history as Deleted.
        ChangeTracker.CascadeDeleteTiming = CascadeTiming.OnSaveChanges;
        ChangeTracker.DeleteOrphansTiming = CascadeTiming.OnSaveChanges;
    }

    public DbSet<TaskItem> Tasks => Set<TaskItem>();
    public DbSet<Category> Categories => Set<Category>();
    public DbSet<TaskComment> TaskComments => Set<TaskComment>();
    public DbSet<TaskHistory> TaskHistories => Set<TaskHistory>();

    protected override void OnModelCreating(ModelBuilder builder)
    {
        base.OnModelCreating(builder);
        builder.ApplyConfigurationsFromAssembly(typeof(AppDbContext).Assembly);

        // Non-SQL Server providers (e.g. SQLite in tests) have no native rowversion generator;
        // allow EF Core to persist application-assigned concurrency tokens instead of storing NULL.
        if (!Database.IsSqlServer())
        {
            builder.Entity<TaskItem>()
                .Property(t => t.RowVersion)
                .IsConcurrencyToken()
                .ValueGeneratedNever();
        }
    }

    public override int SaveChanges(bool acceptAllChangesOnSuccess)
    {
        ConvertTaskDeletesToSoftDelete();
        return base.SaveChanges(acceptAllChangesOnSuccess);
    }

    public override Task<int> SaveChangesAsync(
        bool acceptAllChangesOnSuccess,
        CancellationToken cancellationToken = default)
    {
        ConvertTaskDeletesToSoftDelete();
        return base.SaveChangesAsync(acceptAllChangesOnSuccess, cancellationToken);
    }

    /// <summary>
    /// Ensures a TaskItem is never physically deleted from the database, even if Remove() is called,
    /// and prevents cascading deletes on tracked TaskComments and TaskHistories.
    /// </summary>
    private void ConvertTaskDeletesToSoftDelete()
    {
        var now = DateTime.UtcNow;
        var softDeletedTaskIds = new HashSet<int>();
        var softDeletedTasks = new HashSet<TaskItem>(ReferenceEqualityComparer.Instance);

        foreach (var entry in ChangeTracker.Entries<TaskItem>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.State = EntityState.Unchanged;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = now;
                entry.Entity.UpdatedAt = now;
                entry.Property(t => t.IsDeleted).IsModified = true;
                entry.Property(t => t.DeletedAt).IsModified = true;
                entry.Property(t => t.UpdatedAt).IsModified = true;
                softDeletedTaskIds.Add(entry.Entity.Id);
                softDeletedTasks.Add(entry.Entity);
            }
            else if (entry.State == EntityState.Modified && entry.Entity.IsDeleted && entry.Entity.DeletedAt is null)
            {
                entry.Entity.DeletedAt = now;
                entry.Entity.UpdatedAt = now;
                softDeletedTaskIds.Add(entry.Entity.Id);
                softDeletedTasks.Add(entry.Entity);
            }
        }

        if (softDeletedTaskIds.Count == 0)
            return;

        RevertDeletedDependents<TaskComment>(
            c => softDeletedTaskIds.Contains(c.TaskItemId) || softDeletedTasks.Contains(c.TaskItem));
        RevertDeletedDependents<TaskHistory>(
            h => softDeletedTaskIds.Contains(h.TaskItemId) || softDeletedTasks.Contains(h.TaskItem));
    }

    private void RevertDeletedDependents<TEntity>(Func<TEntity, bool> belongsToSoftDeletedTask)
        where TEntity : class
    {
        foreach (var dependent in ChangeTracker.Entries<TEntity>())
        {
            if (dependent.State == EntityState.Deleted && belongsToSoftDeletedTask(dependent.Entity))
                dependent.State = EntityState.Unchanged;
        }
    }
}
