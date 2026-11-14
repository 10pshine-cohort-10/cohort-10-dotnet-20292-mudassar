namespace TaskManager.Api.DTOs;

/// <summary>
/// Summary counts of tasks grouped by status (Completed, InProgress, Pending, Total).
/// Scoped to user's own/assigned tasks for standard users, or all tasks for Admins.
/// </summary>
public record DashboardSummaryDto(
    int Completed,
    int InProgress,
    int Pending,
    int Total);

