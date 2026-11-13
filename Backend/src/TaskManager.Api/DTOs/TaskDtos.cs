using System.ComponentModel.DataAnnotations;
using TaskManager.Api.Entities;

namespace TaskManager.Api.DTOs;

public record CreateTaskDto(
    [Required, StringLength(200, MinimumLength = 2)] string Title,
    [StringLength(2000)] string? Description = null,
    TaskPriority Priority = TaskPriority.Medium,
    DateTime? DueDate = null,
    int? CategoryId = null,
    Guid? AssignedToId = null);

public record UpdateTaskDto(
    [Required, StringLength(200, MinimumLength = 2)] string Title,
    [StringLength(2000)] string? Description = null,
    TaskItemStatus Status = TaskItemStatus.Pending,
    TaskPriority Priority = TaskPriority.Medium,
    DateTime? DueDate = null,
    int? CategoryId = null,
    Guid? AssignedToId = null,
    string? RowVersion = null);

public record UpdateTaskStatusDto([Required] TaskItemStatus Status);

public record TaskFilterParams(
    int Page = 1,
    int PageSize = 10,
    TaskItemStatus? Status = null,
    TaskPriority? Priority = null,
    int? CategoryId = null,
    Guid? AssignedToId = null,
    string? Search = null);

public record TaskDto(
    int Id,
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueDate,
    int? CategoryId,
    string? CategoryName,
    Guid CreatedById,
    string CreatedByName,
    Guid? AssignedToId,
    string? AssignedToName,
    DateTime CreatedAt,
    DateTime? UpdatedAt,
    string RowVersion);

public record PagedResult<T>(
    IReadOnlyList<T> Items,
    int TotalCount,
    int Page,
    int PageSize)
{
    public int TotalPages => PageSize <= 0 ? 0 : (int)Math.Ceiling(TotalCount / (double)PageSize);
}

