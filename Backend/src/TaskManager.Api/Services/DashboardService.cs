using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Services;

public interface IDashboardService
{
    Task<DashboardSummaryDto> GetSummaryAsync(Guid userId, bool isAdmin);
}

public class DashboardService(AppDbContext db, ILogger<DashboardService> log) : IDashboardService
{
    public async Task<DashboardSummaryDto> GetSummaryAsync(Guid userId, bool isAdmin)
    {
        // Soft-deleted tasks are excluded by the global query filter.
        var query = db.Tasks.AsNoTracking();
        if (!isAdmin)
            query = query.Where(t => t.CreatedById == userId || t.AssignedToId == userId);

        var counts = await query
            .GroupBy(t => t.Status)
            .Select(g => new { Status = g.Key, Count = g.Count() })
            .ToListAsync();

        int Count(TaskItemStatus s) => counts.FirstOrDefault(c => c.Status == s)?.Count ?? 0;

        var summary = new DashboardSummaryDto(
            Completed: Count(TaskItemStatus.Completed),
            InProgress: Count(TaskItemStatus.InProgress),
            Pending: Count(TaskItemStatus.Pending),
            Total: counts.Sum(c => c.Count));

        log.LogDebug("Dashboard summary for user {UserId} (admin: {IsAdmin}): {@Summary}", userId, isAdmin, summary);
        return summary;
    }
}

