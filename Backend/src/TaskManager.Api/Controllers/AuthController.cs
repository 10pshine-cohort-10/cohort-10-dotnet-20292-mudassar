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
    [ProducesResponseType<RegisterResponse>(StatusCodes.Status202Accepted)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    public async Task<ActionResult<RegisterResponse>> Register(RegisterDto dto)
    {
        await auth.RegisterAsync(dto);
        return Accepted(new RegisterResponse(
            "If the provided details are valid, registration has been accepted. You can now sign in."));
    }

    [HttpPost("login"), AllowAnonymous, EnableRateLimiting(AuthExtensions.AuthRateLimitPolicy)]
    [ProducesResponseType<AuthResponse>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<AuthResponse>> Login(LoginDto dto) => Ok(await auth.LoginAsync(dto));

    [HttpGet("me"), Authorize]
    [ProducesResponseType<UserInfoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserInfoDto>> Me() => Ok(await auth.GetCurrentAsync(User.GetUserId()));

    [HttpPut("me"), Authorize]
    [ProducesResponseType<UserInfoDto>(StatusCodes.Status200OK)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status400BadRequest)]
    [ProducesResponseType<ProblemDetails>(StatusCodes.Status401Unauthorized)]
    public async Task<ActionResult<UserInfoDto>> UpdateMe(UpdateProfileDto dto)
        => Ok(await auth.UpdateProfileAsync(User.GetUserId(), dto));

    [HttpPost("logout"), Authorize]
    [ProducesResponseType(StatusCodes.Status204NoContent)]
    public IActionResult Logout()
    {
        log.LogInformation("User {UserId} logged out", User.GetUserId());
        return NoContent();      // JWT is stateless: the client discards the token
    }
}

