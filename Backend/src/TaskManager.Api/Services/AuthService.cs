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
    Task<UserInfoDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto);
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
        {
            log.LogWarning("Registration rejected: email is already registered");
            throw new ConflictException("This email is already registered.");
        }

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = dto.FullName.Trim()
        };

        var result = await users.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            log.LogWarning("Registration failed validation with {ErrorCount} error(s)", result.Errors.Count());
            throw new AppValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        var roleResult = await users.AddToRoleAsync(user, Roles.User);   // public sign-up is ALWAYS "User"
        if (!roleResult.Succeeded)
        {
            await users.DeleteAsync(user);
            log.LogWarning("Registration failed assigning role {Role} to user {UserId}", Roles.User, user.Id);
            throw new AppValidationException(string.Join(" ", roleResult.Errors.Select(e => e.Description)));
        }

        log.LogInformation("User {UserId} registered with role {Role}", user.Id, Roles.User);

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
            throw new UnauthorizedException(InvalidCredentials);   // same message = no lockout enumeration
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

        log.LogInformation("User {UserId} logged in with roles {Roles}", user.Id, roles);
        return new AuthResponse(token, expires, new UserInfoDto(user.Id, user.FullName, user.Email!, roles));
    }

    public async Task<UserInfoDto> GetCurrentAsync(Guid userId)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            log.LogWarning("Profile lookup failed: user {UserId} was not found", userId);
            throw new NotFoundException("User was not found.");
        }

        log.LogDebug("Fetched current profile for user {UserId}", userId);
        return new UserInfoDto(user.Id, user.FullName, user.Email!, await users.GetRolesAsync(user));
    }

    public async Task<UserInfoDto> UpdateProfileAsync(Guid userId, UpdateProfileDto dto)
    {
        var user = await users.FindByIdAsync(userId.ToString());
        if (user is null)
        {
            log.LogWarning("Profile update failed: user {UserId} was not found", userId);
            throw new NotFoundException("User was not found.");
        }

        var trimmedName = dto.FullName?.Trim() ?? string.Empty;
        if (trimmedName.Length < 2 || trimmedName.Length > 100)
            throw new AppValidationException("Full name must be between 2 and 100 characters.");

        user.FullName = trimmedName;
        var result = await users.UpdateAsync(user);
        if (!result.Succeeded)
            throw new AppValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));

        log.LogInformation("User {UserId} updated profile full name", userId);
        return new UserInfoDto(user.Id, user.FullName, user.Email!, await users.GetRolesAsync(user));
    }
}

