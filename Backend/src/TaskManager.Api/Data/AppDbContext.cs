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

        var softDeletedTaskIds = new HashSet<int>();
        var softDeletedTasks = new HashSet<TaskItem>(ReferenceEqualityComparer.Instance);

        foreach (var entry in ChangeTracker.Entries<TaskItem>().ToList())
        {
            if (entry.State == EntityState.Deleted || (entry.State == EntityState.Modified && entry.Entity.IsDeleted))
            {
                if (entry.Entity.Id != 0)
                {
                    softDeletedTaskIds.Add(entry.Entity.Id);
                }

                softDeletedTasks.Add(entry.Entity);
            }
        }

        // Capture newly Added dependents belonging to tasks being soft-deleted before CascadeChanges()
        // runs, because EF Core detaches Added dependents when their principal is in EntityState.Deleted.
        var addedCommentsToPreserve = CaptureAddedDependents<TaskComment>(
            nameof(TaskComment.TaskItemId),
            c => c.TaskItem,
            softDeletedTaskIds,
            softDeletedTasks);

        var addedHistoriesToPreserve = CaptureAddedDependents<TaskHistory>(
            nameof(TaskHistory.TaskItemId),
            h => h.TaskItem,
            softDeletedTaskIds,
            softDeletedTasks);

        // Resolve any pending cascade/orphan deletions now so dependents of soft-deleted tasks
        // can be restored before base.SaveChanges executes, while preserving orphan deletes for active tasks.
        ChangeTracker.CascadeChanges();

        var now = DateTime.UtcNow;

        foreach (var entry in ChangeTracker.Entries<TaskItem>().ToList())
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
                if (!Database.IsSqlServer())
                {
                    entry.Entity.RowVersion = Guid.NewGuid().ToByteArray()[..8];
                    entry.Property(t => t.RowVersion).IsModified = true;
                }
                if (entry.Entity.Id != 0)
                {
                    softDeletedTaskIds.Add(entry.Entity.Id);
                }

                softDeletedTasks.Add(entry.Entity);
            }
            else if (entry.State == EntityState.Modified && entry.Entity.IsDeleted)
            {
                var deletedAt = entry.Entity.DeletedAt ?? now;
                entry.CurrentValues.SetValues(entry.OriginalValues);
                entry.State = EntityState.Unchanged;
                entry.Entity.IsDeleted = true;
                entry.Entity.DeletedAt = deletedAt;
                entry.Entity.UpdatedAt = now;
                entry.Property(t => t.IsDeleted).IsModified = true;
                entry.Property(t => t.DeletedAt).IsModified = true;
                entry.Property(t => t.UpdatedAt).IsModified = true;
                if (!Database.IsSqlServer())
                {
                    entry.Entity.RowVersion = Guid.NewGuid().ToByteArray()[..8];
                    entry.Property(t => t.RowVersion).IsModified = true;
                }

                if (entry.Entity.Id != 0)
                {
                    softDeletedTaskIds.Add(entry.Entity.Id);
                }

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

        RestoreAddedDependents(
            addedCommentsToPreserve,
            softDeletedTasks,
            (comment, taskId, parentTask) =>
            {
                if (taskId != 0)
                {
                    comment.TaskItemId = taskId;
                }

                if (parentTask is not null)
                {
                    comment.TaskItem = parentTask;
                    if (!parentTask.Comments.Contains(comment))
                    {
                        parentTask.Comments.Add(comment);
                    }
                }
            });

        RestoreAddedDependents(
            addedHistoriesToPreserve,
            softDeletedTasks,
            (history, taskId, parentTask) =>
            {
                if (taskId != 0)
                {
                    history.TaskItemId = taskId;
                }

                if (parentTask is not null)
                {
                    history.TaskItem = parentTask;
                    if (!parentTask.History.Contains(history))
                    {
                        parentTask.History.Add(history);
                    }
                }
            });
    }

    private List<(TEntity Entity, int TaskItemId, TaskItem? ParentTask)> CaptureAddedDependents<TEntity>(
        string foreignKeyPropertyName,
        Func<TEntity, TaskItem?> getParentTask,
        HashSet<int> softDeletedTaskIds,
        HashSet<TaskItem> softDeletedTasks)
        where TEntity : class
    {
        var captured = new List<(TEntity Entity, int TaskItemId, TaskItem? ParentTask)>();
        if (softDeletedTaskIds.Count == 0 && softDeletedTasks.Count == 0)
        {
            return captured;
        }

        foreach (var entry in ChangeTracker.Entries<TEntity>().ToList())
        {
            if (entry.State != EntityState.Added)
            {
                continue;
            }

            var currentTaskId = entry.CurrentValues.GetValue<int>(foreignKeyPropertyName);
            var originalTaskId = entry.OriginalValues.GetValue<int>(foreignKeyPropertyName);
            var taskId = currentTaskId != 0 ? currentTaskId : originalTaskId;
            var parentTask = getParentTask(entry.Entity)
                ?? (taskId != 0 ? softDeletedTasks.FirstOrDefault(t => t.Id == taskId) : null);

            if ((taskId != 0 && softDeletedTaskIds.Contains(taskId))
                || (parentTask is not null && softDeletedTasks.Contains(parentTask)))
            {
                captured.Add((entry.Entity, taskId != 0 ? taskId : parentTask?.Id ?? 0, parentTask));
            }
        }

        return captured;
    }

    private void RestoreAddedDependents<TEntity>(
        List<(TEntity Entity, int TaskItemId, TaskItem? ParentTask)> capturedDependents,
        HashSet<TaskItem> softDeletedTasks,
        Action<TEntity, int, TaskItem?> restoreRelationship)
        where TEntity : class
    {
        foreach (var (entity, taskId, capturedParent) in capturedDependents)
        {
            var parentTask = capturedParent
                ?? (taskId != 0 ? softDeletedTasks.FirstOrDefault(t => t.Id == taskId) : null);
            restoreRelationship(entity, taskId, parentTask);
            var entry = Entry(entity);
            if (entry.State != EntityState.Added)
            {
                entry.State = EntityState.Added;
            }
        }
    }

    private void RevertDeletedDependents<TEntity>(
        Func<EntityEntry<TEntity>, bool> belongsToSoftDeletedTask,
        Action<EntityEntry<TEntity>> restoreRelationship)
        where TEntity : class
    {
        foreach (var dependent in ChangeTracker.Entries<TEntity>().ToList())
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
