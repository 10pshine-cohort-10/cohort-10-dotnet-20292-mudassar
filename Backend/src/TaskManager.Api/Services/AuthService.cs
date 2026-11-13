using Microsoft.AspNetCore.Identity;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;

namespace TaskManager.Api.Services;

public interface IAuthService
{
    Task<UserInfoDto> RegisterAsync(RegisterDto dto);
    Task<AuthResponse> LoginAsync(LoginDto dto);
    Task<UserInfoDto> GetCurrentAsync(Guid userId);
}

public class AuthService(
    UserManager<ApplicationUser> users,
    IJwtTokenService jwt,
    ILogger<AuthService> log) : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password.";

    public async Task<UserInfoDto> RegisterAsync(RegisterDto dto)
    {
        var email = dto.Email.Trim();

        if (await users.FindByEmailAsync(email) is not null)
            throw new ConflictException("This email is already registered.");

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = dto.FullName.Trim()
        };

        var result = await users.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
            throw new AppValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        await users.AddToRoleAsync(user, Roles.User);   // public sign-up is ALWAYS "User"
        log.LogInformation("User {UserId} registered", user.Id);

        return new UserInfoDto(user.Id, user.FullName, user.Email!, [Roles.User]);
    }

    public async Task<AuthResponse> LoginAsync(LoginDto dto)
    {
        var user = await users.FindByEmailAsync(dto.Email.Trim());
        if (user is null || !user.IsActive)
        {
            log.LogWarning("Failed login: unknown or inactive user");
            throw new UnauthorizedException(InvalidCredentials);   // same message = no user enumeration
        }

        if (await users.IsLockedOutAsync(user))
        {
            log.LogWarning("Login attempt for locked-out user {UserId}", user.Id);
            throw new UnauthorizedException("Account is temporarily locked. Try again later.");
        }

        if (!await users.CheckPasswordAsync(user, dto.Password))
        {
            await users.AccessFailedAsync(user);              // counts toward lockout
            log.LogWarning("Failed login for user {UserId}: wrong password", user.Id);
            throw new UnauthorizedException(InvalidCredentials);
        }

        await users.ResetAccessFailedCountAsync(user);
        var roles = await users.GetRolesAsync(user);
        var (token, expires) = jwt.Create(user, roles);

        log.LogInformation("User {UserId} logged in", user.Id);
        return new AuthResponse(token, expires, new UserInfoDto(user.Id, user.FullName, user.Email!, roles));
    }

    public async Task<UserInfoDto> GetCurrentAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString())
                   ?? throw new NotFoundException("User was not found.");

        return new UserInfoDto(user.Id, user.FullName, user.Email!, await users.GetRolesAsync(user));
    }
}

