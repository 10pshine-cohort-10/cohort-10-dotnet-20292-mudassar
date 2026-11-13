using Microsoft.AspNetCore.Identity;

namespace TaskManager.Api.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;

    public ICollection<TaskItem> CreatedTasks { get; set; } = new List<TaskItem>();
    public ICollection<TaskItem> AssignedTasks { get; set; } = new List<TaskItem>();
    public ICollection<TaskComment> TaskComments { get; set; } = new List<TaskComment>();
    public ICollection<TaskHistory> TaskHistories { get; set; } = new List<TaskHistory>();
}
