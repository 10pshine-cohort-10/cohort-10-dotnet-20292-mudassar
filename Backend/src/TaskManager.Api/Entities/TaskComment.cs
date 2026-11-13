namespace TaskManager.Api.Entities;

public class TaskComment
{
    public int Id { get; set; }

    public int TaskItemId { get; set; }
    public TaskItem TaskItem { get; set; } = default!;

    public Guid UserId { get; set; }
    public ApplicationUser User { get; set; } = default!;

    public string Text { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
}

