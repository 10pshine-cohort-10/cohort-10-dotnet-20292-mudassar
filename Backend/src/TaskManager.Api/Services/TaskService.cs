using System.Diagnostics.CodeAnalysis;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;

namespace TaskManager.Api.Services;

public interface ITaskService
{
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Query filter parameters with optional defaults for task listing.")]
    Task<PagedResult<TaskDto>> GetAllAsync(
        Guid userId,
        bool isAdmin,
        int page = 1,
        int pageSize = 10,
        TaskItemStatus? status = null,
        TaskPriority? priority = null,
        int? categoryId = null,
        Guid? assignedToId = null,
        string? search = null);
    Task<TaskDto> GetByIdAsync(int id, Guid userId, bool isAdmin);
    Task<TaskDto> CreateAsync(CreateTaskDto dto, Guid userId);
    Task<TaskDto> UpdateAsync(int id, UpdateTaskDto dto, Guid userId, bool isAdmin);
    Task<TaskDto> UpdateStatusAsync(int id, TaskItemStatus status, Guid userId, bool isAdmin);
    Task DeleteAsync(int id, Guid userId, bool isAdmin);
}

public class TaskService(
    AppDbContext db,
    ILogger<TaskService> log) : ITaskService
{
    private const string TaskNotFoundMessage = "Task was not found.";

    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Query filter parameters with optional defaults for task listing.")]
    [SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "EF Core LINQ translator requires parameterless ToLower() to emit SQL LOWER().")]
    [SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "EF Core LINQ translator does not translate ToLowerInvariant().")]
    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "EF Core LINQ translator does not translate StringComparison overloads to SQL.")]
    public async Task<PagedResult<TaskDto>> GetAllAsync(
        Guid userId,
        bool isAdmin,
        int page = 1,
        int pageSize = 10,
        TaskItemStatus? status = null,
        TaskPriority? priority = null,
        int? categoryId = null,
        Guid? assignedToId = null,
        string? search = null)
    {
        page = Math.Max(1, page);
        pageSize = Math.Clamp(pageSize, 1, 100);

        var query = db.Tasks.AsNoTracking();

        if (!isAdmin)
            query = query.Where(t => t.CreatedById == userId || t.AssignedToId == userId);

        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);

        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);

        if (categoryId.HasValue)
            query = query.Where(t => t.CategoryId == categoryId.Value);

        if (assignedToId.HasValue)
            query = query.Where(t => t.AssignedToId == assignedToId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t =>
                t.Title.ToLower().Contains(term) ||
                (t.Description != null && t.Description.ToLower().Contains(term)));
        }

        var totalCount = await query.CountAsync();
        var offset = (long)(page - 1) * pageSize;
        if (offset > int.MaxValue || offset >= totalCount)
            return new PagedResult<TaskDto>([], totalCount, page, pageSize);

        var entities = await query
            .Include(t => t.Category)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .OrderByDescending(t => t.CreatedAt)
            .Skip((int)offset)
            .Take(pageSize)
            .ToListAsync();

        var items = entities.Select(MapToDto).ToList();

        return new PagedResult<TaskDto>(items, totalCount, page, pageSize);
    }

    public async Task<TaskDto> GetByIdAsync(int id, Guid userId, bool isAdmin)
    {
        var task = await db.Tasks
            .AsNoTracking()
            .Include(t => t.Category)
            .Include(t => t.CreatedBy)
            .Include(t => t.AssignedTo)
            .FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException(TaskNotFoundMessage);

        EnsureAccess(task, userId, isAdmin);
        return MapToDto(task);
    }

    public async Task<TaskDto> CreateAsync(CreateTaskDto dto, Guid userId)
    {
        var title = ValidateTitle(dto.Title);
        ValidatePriority(dto.Priority);

        if (dto.DueDate.HasValue && dto.DueDate.Value.Date < DateTime.UtcNow.Date)
            throw new AppValidationException("Due date cannot be in the past.");

        await ValidateReferencesAsync(dto.CategoryId, dto.AssignedToId);

        var task = new TaskItem
        {
            Title = title,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim(),
            Status = TaskItemStatus.Pending,
            Priority = dto.Priority,
            DueDate = dto.DueDate,
            CategoryId = dto.CategoryId,
            CreatedById = userId,
            AssignedToId = dto.AssignedToId,
            CreatedAt = DateTime.UtcNow,
            RowVersion = db.Database.IsSqlServer() ? [] : Guid.NewGuid().ToByteArray()[..8]
        };

        db.Tasks.Add(task);
        db.TaskHistories.Add(new TaskHistory
        {
            TaskItem = task,
            UserId = userId,
            Action = "Created",
            NewValue = task.Status.ToString()
        });

        await db.SaveChangesAsync();

        log.LogInformation("User {UserId} created task {TaskId} ({Title})", userId, task.Id, task.Title);
        return await GetByIdAsync(task.Id, userId, isAdmin: true);
    }

    public async Task<TaskDto> UpdateAsync(int id, UpdateTaskDto dto, Guid userId, bool isAdmin)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException(TaskNotFoundMessage);

        EnsureAccess(task, userId, isAdmin);

        var title = ValidateTitle(dto.Title);
        ValidateStatus(dto.Status);
        ValidatePriority(dto.Priority);
        await ValidateReferencesAsync(dto.CategoryId, dto.AssignedToId);

        if (!string.IsNullOrWhiteSpace(dto.RowVersion))
        {
            byte[] clientVersion;
            try
            {
                clientVersion = Convert.FromBase64String(dto.RowVersion);
            }
            catch (FormatException)
            {
                throw new AppValidationException("Invalid RowVersion format.");
            }

            if (task.RowVersion is { Length: > 0 } && !task.RowVersion.SequenceEqual(clientVersion))
                throw new ConflictException("The task was modified by another user. Please reload and try again.");

            db.Entry(task).Property(t => t.RowVersion).OriginalValue = clientVersion;
        }

        var oldStatus = task.Status;

        task.Title = title;
        task.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();
        task.Status = dto.Status;
        task.Priority = dto.Priority;
        task.DueDate = dto.DueDate;
        task.CategoryId = dto.CategoryId;
        task.AssignedToId = dto.AssignedToId;
        task.UpdatedAt = DateTime.UtcNow;

        if (!db.Database.IsSqlServer())
            task.RowVersion = Guid.NewGuid().ToByteArray()[..8];

        db.TaskHistories.Add(new TaskHistory
        {
            TaskItemId = task.Id,
            UserId = userId,
            Action = "Updated",
            OldValue = oldStatus.ToString(),
            NewValue = task.Status.ToString()
        });

        await SaveWithConcurrencyGuardAsync(task.Id);

        log.LogInformation("User {UserId} updated task {TaskId}", userId, task.Id);
        return await GetByIdAsync(task.Id, userId, isAdmin: true);
    }

    public async Task<TaskDto> UpdateStatusAsync(int id, TaskItemStatus status, Guid userId, bool isAdmin)
    {
        ValidateStatus(status);

        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException(TaskNotFoundMessage);

        EnsureAccess(task, userId, isAdmin);

        var oldStatus = task.Status;
        task.Status = status;
        task.UpdatedAt = DateTime.UtcNow;

        if (!db.Database.IsSqlServer())
            task.RowVersion = Guid.NewGuid().ToByteArray()[..8];

        db.TaskHistories.Add(new TaskHistory
        {
            TaskItemId = task.Id,
            UserId = userId,
            Action = "StatusChanged",
            OldValue = oldStatus.ToString(),
            NewValue = status.ToString()
        });

        await SaveWithConcurrencyGuardAsync(task.Id);

        log.LogInformation(
            "User {UserId} changed task {TaskId} status from {OldStatus} to {NewStatus}",
            userId, task.Id, oldStatus, status);

        return await GetByIdAsync(task.Id, userId, isAdmin: true);
    }

    public async Task DeleteAsync(int id, Guid userId, bool isAdmin)
    {
        var task = await db.Tasks.FirstOrDefaultAsync(t => t.Id == id)
            ?? throw new NotFoundException(TaskNotFoundMessage);

        if (!isAdmin && task.CreatedById != userId)
            throw new ForbiddenException("Only the task creator or an admin can delete this task.");

        task.IsDeleted = true;
        task.DeletedAt = DateTime.UtcNow;
        task.UpdatedAt = DateTime.UtcNow;

        db.TaskHistories.Add(new TaskHistory
        {
            TaskItemId = task.Id,
            UserId = userId,
            Action = "Deleted",
            OldValue = task.Title
        });

        await db.SaveChangesAsync();

        log.LogWarning("User {UserId} soft-deleted task {TaskId}", userId, task.Id);
    }

    private static void EnsureAccess(TaskItem task, Guid userId, bool isAdmin)
    {
        if (!isAdmin && task.CreatedById != userId && task.AssignedToId != userId)
            throw new ForbiddenException("You do not have permission to access this task.");
    }

    private static string ValidateTitle(string? rawTitle)
    {
        var title = rawTitle?.Trim() ?? string.Empty;
        if (title.Length < 2)
            throw new AppValidationException("Task title must be at least 2 characters.");
        return title;
    }

    private static void ValidatePriority(TaskPriority priority)
    {
        if (!Enum.IsDefined(priority))
            throw new AppValidationException("Invalid task priority value.");
    }

    private static void ValidateStatus(TaskItemStatus status)
    {
        if (!Enum.IsDefined(status))
            throw new AppValidationException("Invalid task status value.");
    }

    private async Task ValidateReferencesAsync(int? categoryId, Guid? assignedToId)
    {
        if (categoryId.HasValue && !await db.Categories.AnyAsync(c => c.Id == categoryId.Value))
            throw new AppValidationException("Selected category does not exist.");

        if (assignedToId.HasValue)
        {
            var assignee = await db.Users.AsNoTracking().FirstOrDefaultAsync(u => u.Id == assignedToId.Value);
            if (assignee is null || !assignee.IsActive)
                throw new AppValidationException("Assigned user does not exist or is inactive.");
        }
    }

    private async Task SaveWithConcurrencyGuardAsync(int taskId)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateConcurrencyException ex)
        {
            log.LogWarning(ex, "Concurrency conflict while updating task {TaskId}", taskId);
            throw new ConflictException("The task was modified by another user. Please reload and try again.");
        }
    }

    private static TaskDto MapToDto(TaskItem t) => new(
        t.Id,
        t.Title,
        t.Description,
        t.Status,
        t.Priority,
        t.DueDate,
        t.CategoryId,
        t.Category?.Name,
        t.CreatedById,
        t.CreatedBy?.FullName ?? string.Empty,
        t.AssignedToId,
        t.AssignedTo?.FullName,
        t.CreatedAt,
        t.UpdatedAt,
        Convert.ToBase64String(t.RowVersion ?? []));
}
