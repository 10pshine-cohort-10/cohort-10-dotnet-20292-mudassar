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
    /// Ensures a tracked TaskItem saved through AppDbContext is soft-deleted rather than physically deleted,
    /// discarding pending non-deletion edits and preserving tracked TaskComments and TaskHistories.
    /// </summary>
    private void ConvertTaskDeletesToSoftDelete()
    {
        if (ChangeTracker.AutoDetectChangesEnabled)
        {
            ChangeTracker.DetectChanges();
        }

        // Resolve any pending cascade/orphan deletions now so dependents of soft-deleted tasks
        // can be restored before base.SaveChanges executes, while preserving orphan deletes for active tasks.
        ChangeTracker.CascadeChanges();

        var now = DateTime.UtcNow;
        var softDeletedTaskIds = new HashSet<int>();
        var softDeletedTasks = new HashSet<TaskItem>(ReferenceEqualityComparer.Instance);

        foreach (var entry in ChangeTracker.Entries<TaskItem>())
        {
            if (entry.State == EntityState.Deleted)
            {
                entry.CurrentValues.SetValues(entry.OriginalValues);
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
            else if (entry.State == EntityState.Modified && entry.Entity.IsDeleted)
            {
                if (entry.Entity.DeletedAt is null)
                {
                    entry.Entity.DeletedAt = now;
                    entry.Entity.UpdatedAt = now;
                }

                softDeletedTaskIds.Add(entry.Entity.Id);
                softDeletedTasks.Add(entry.Entity);
            }
        }

        if (softDeletedTaskIds.Count == 0 && softDeletedTasks.Count == 0)
            return;

        RevertDeletedDependents<TaskComment>(
            dependent =>
            {
                var currentTaskId = dependent.CurrentValues.GetValue<int>(nameof(TaskComment.TaskItemId));
                var originalTaskId = dependent.OriginalValues.GetValue<int>(nameof(TaskComment.TaskItemId));
                return (currentTaskId != 0 && softDeletedTaskIds.Contains(currentTaskId))
                    || (originalTaskId != 0 && softDeletedTaskIds.Contains(originalTaskId))
                    || (dependent.Entity.TaskItem is not null && softDeletedTasks.Contains(dependent.Entity.TaskItem));
            },
            dependent =>
            {
                var taskId = dependent.CurrentValues.GetValue<int>(nameof(TaskComment.TaskItemId));
                var parentTask = dependent.Entity.TaskItem
                    ?? softDeletedTasks.FirstOrDefault(t => t.Id != 0 && t.Id == taskId);
                if (parentTask is not null)
                {
                    dependent.Entity.TaskItem = parentTask;
                    if (!parentTask.Comments.Contains(dependent.Entity))
                    {
                        parentTask.Comments.Add(dependent.Entity);
                    }
                }
            });

        RevertDeletedDependents<TaskHistory>(
            dependent =>
            {
                var currentTaskId = dependent.CurrentValues.GetValue<int>(nameof(TaskHistory.TaskItemId));
                var originalTaskId = dependent.OriginalValues.GetValue<int>(nameof(TaskHistory.TaskItemId));
                return (currentTaskId != 0 && softDeletedTaskIds.Contains(currentTaskId))
                    || (originalTaskId != 0 && softDeletedTaskIds.Contains(originalTaskId))
                    || (dependent.Entity.TaskItem is not null && softDeletedTasks.Contains(dependent.Entity.TaskItem));
            },
            dependent =>
            {
                var taskId = dependent.CurrentValues.GetValue<int>(nameof(TaskHistory.TaskItemId));
                var parentTask = dependent.Entity.TaskItem
                    ?? softDeletedTasks.FirstOrDefault(t => t.Id != 0 && t.Id == taskId);
                if (parentTask is not null)
                {
                    dependent.Entity.TaskItem = parentTask;
                    if (!parentTask.History.Contains(dependent.Entity))
                    {
                        parentTask.History.Add(dependent.Entity);
                    }
                }
            });
    }

    private void RevertDeletedDependents<TEntity>(
        Func<EntityEntry<TEntity>, bool> belongsToSoftDeletedTask,
        Action<EntityEntry<TEntity>> restoreRelationship)
        where TEntity : class
    {
        foreach (var dependent in ChangeTracker.Entries<TEntity>())
        {
            if (dependent.State == EntityState.Deleted && belongsToSoftDeletedTask(dependent))
            {
                dependent.CurrentValues.SetValues(dependent.OriginalValues);
                dependent.State = EntityState.Unchanged;
                restoreRelationship(dependent);
            }
        }
    }
}
