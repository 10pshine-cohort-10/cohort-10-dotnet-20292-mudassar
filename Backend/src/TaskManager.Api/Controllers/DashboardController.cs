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
public class DashboardController(IDashboardService dashboard) : ControllerBase
{
    /// <summary>Task counts by status: own/assigned tasks for users, all tasks for admins.</summary>
    [HttpGet("summary")]
    [ProducesResponseType<DashboardSummaryDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<DashboardSummaryDto>> GetSummary()
        => Ok(await dashboard.GetSummaryAsync(User.GetUserId(), User.IsAdmin()));
}

