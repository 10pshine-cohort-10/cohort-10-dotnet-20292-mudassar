using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManager.Api.DTOs;
using TaskManager.Api.Extensions;
using TaskManager.Api.Services;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/dashboard")]
[Produces("application/json")]
[Authorize]
public class DashboardController : ControllerBase
{
    private readonly IDashboardService dashboard;

    public DashboardController(IDashboardService dashboard)
    {
        ArgumentNullException.ThrowIfNull(dashboard);
        this.dashboard = dashboard;
    }
    /// <summary>
    /// Returns Completed, InProgress, Pending, and Total task counts:
    /// user-specific counts for standard users, or all task counts for Admins.
    /// </summary>
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary()
        => Ok(await dashboard.GetSummaryAsync(User.GetUserId(), User.IsAdmin()));
}

