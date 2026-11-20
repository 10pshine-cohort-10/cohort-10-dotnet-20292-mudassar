namespace TaskManager.Api.Entities;

public class Category
{
    public int Id { get; set; }
    public string Name { get; set; } = default!;
    public string? Description { get; set; }

    public ICollection<TaskItem> Tasks { get; set; } = new List<TaskItem>();
}

