using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.RateLimiting;
using TaskManager.Api.DTOs;
using TaskManager.Api.Extensions;
using TaskManager.Api.Services;

namespace TaskManager.Api.Controllers;

[ApiController]
[Route("api/auth")]
[Produces("application/json")]
public class AuthController(IAuthService auth, ILogger<AuthController> log) : ControllerBase
{
    [HttpPost("register"), AllowAnonymous, EnableRateLimiting(AuthExtensions.AuthRateLimitPolicy)]
    [ProducesResponseType<UserInfoDto>(StatusCodes.Status201Created)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status409Conflict)]
    public async Task<ActionResult<UserInfoDto>> Register(RegisterDto dto)
    {
        var user = await auth.RegisterAsync(dto);
        return CreatedAtAction(nameof(Me), null, user);
    }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting(AuthExtensions.AuthRateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginDto dto) => Ok(await auth.LoginAsync(dto));

    [HttpGet("me"), Authorize]
    [ProducesResponseType<UserInfoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserInfoDto>> Me() => Ok(await auth.GetCurrentAsync(User.GetUserId()));

    [HttpPost("logout"), Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        log.LogInformation("User {UserId} logged out", User.GetUserId());
        return NoContent();      // JWT is stateless: the client discards the token
    }
}

