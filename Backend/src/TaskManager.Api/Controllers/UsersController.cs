using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using TaskManager.Api.DTOs;
using TaskManager.Api.Extensions;
using TaskManager.Api.Services;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/users")]
[Produces("application/json")]
[Authorize]
public class UsersController(IUserService users) : ControllerBase
{
    [HttpGet, Authorize(Policy = AuthExtensions.AdminOnlyPolicy)]
    [ProducesResponseType<IReadOnlyList<UserDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    public async Task<ActionResult<IReadOnlyList<UserDto>>> GetAll(
        [FromQuery] string? search = null,
        [FromQuery] bool? activeOnly = null)
        => Ok(await users.GetAllAsync(search, activeOnly));

    [HttpGet("lookup")]
    [ProducesResponseType<IReadOnlyList<UserLookupDto>>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<IReadOnlyList<UserLookupDto>>> GetLookup()
        => Ok(await users.GetLookupAsync());

    [HttpGet("{id:guid}"), Authorize(Policy = AuthExtensions.AdminOnlyPolicy)]
    [ProducesResponseType<UserDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<ActionResult<UserDto>> GetById(Guid id)
        => Ok(await users.GetByIdAsync(id));

    [HttpPut("{id:guid}/role"), Authorize(Policy = AuthExtensions.AdminOnlyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> ChangeRole(Guid id, ChangeRoleDto dto)
    {
        await users.ChangeRoleAsync(id, dto.Role, User.GetUserId());
        return NoContent();
    }

    [HttpPut("{id:guid}/active"), Authorize(Policy = AuthExtensions.AdminOnlyPolicy)]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status403Forbidden)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status404NotFound)]
    public async Task<IActionResult> SetActive(Guid id, SetActiveDto dto)
    {
        await users.SetActiveAsync(id, dto.IsActive, User.GetUserId());
        return NoContent();
    }
}

