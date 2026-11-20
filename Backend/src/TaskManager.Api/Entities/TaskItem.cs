using System.ComponentModel.DataAnnotations;

namespace TaskManager.Api.Entities;

public class TaskItem
{
    public int Id { get; set; }
    public string Title { get; set; } = default!;
    public string? Description { get; set; }
    public TaskItemStatus Status { get; set; } = TaskItemStatus.Pending;
    public TaskPriority Priority { get; set; } = TaskPriority.Medium;
    public DateTime? DueDate { get; set; }

    public int? CategoryId { get; set; }
    public Category? Category { get; set; }

    public Guid CreatedById { get; set; }
    public ApplicationUser CreatedBy { get; set; } = default!;

    public Guid? AssignedToId { get; set; }
    public ApplicationUser? AssignedTo { get; set; }

    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public DateTime? UpdatedAt { get; set; }

    public bool IsDeleted { get; set; }
    public DateTime? DeletedAt { get; set; }

    [Timestamp]
    public byte[] RowVersion { get; set; } = [];

    public ICollection<TaskComment> Comments { get; set; } = new List<TaskComment>();
    public ICollection<TaskHistory> History { get; set; } = new List<TaskHistory>();
}

