namespace TaskManager.Api.DTOs;

public record DashboardSummaryDto(int Completed, int InProgress, int Pending, int Total);

