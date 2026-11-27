using TaskManager.Api.Entities;

namespace TaskManager.Api.DTOs;

public record TaskStatusUpdatedEvent(
    int TaskId,
    string Title,
    TaskItemStatus OldStatus,
    TaskItemStatus NewStatus,
    DateTime UpdatedAt,
    string RowVersion,
    Guid UpdatedById,
    Guid CreatedById,
    Guid? AssignedToId);

