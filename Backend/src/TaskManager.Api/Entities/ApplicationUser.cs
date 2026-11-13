using Microsoft.AspNetCore.Identity;

namespace TaskManager.Api.Entities;

public class ApplicationUser : IdentityUser<Guid>
{
    public string FullName { get; set; } = default!;
    public DateTime CreatedAt { get; set; } = DateTime.UtcNow;
    public bool IsActive { get; set; } = true;
}

