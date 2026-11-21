using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
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
    ILogger<AuthService> log,
    AppDbContext? db = null) : IAuthService
{
    private const string InvalidCredentials = "Invalid email or password.";

    public async Task<UserInfoDto> RegisterAsync(RegisterDto dto)
    {
        var email = dto.Email.Trim();
        var fullName = dto.FullName.Trim();

        var user = new ApplicationUser
        {
            UserName = email,
            Email = email,
            FullName = fullName
        };

        // Run password validators before checking email existence so weak passwords produce
        // the exact same validation response for both existing and unknown email addresses.
        await ValidatePasswordAsync(user, dto.Password);

        if (await users.FindByEmailAsync(email) is not null)
        {
            log.LogWarning("Registration ignored: email is already registered");
            return new UserInfoDto(Guid.Empty, fullName, email, [Roles.User]);
        }

        await using var tx = db is not null && db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync()
            : null;

        var result = await users.CreateAsync(user, dto.Password);
        if (!result.Succeeded)
        {
            if (tx is not null)
                await tx.RollbackAsync();

            var isDuplicate = result.Errors.Any(e =>
                string.Equals(e.Code, "DuplicateEmail", StringComparison.OrdinalIgnoreCase)
                || string.Equals(e.Code, "DuplicateUserName", StringComparison.OrdinalIgnoreCase));

            if (isDuplicate)
            {
                log.LogWarning("Registration ignored after concurrent duplicate email check");
                return new UserInfoDto(Guid.Empty, fullName, email, [Roles.User]);
            }

            log.LogWarning("Registration failed validation with {ErrorCount} error(s)", result.Errors.Count());
            throw new AppValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));
        }

        var roleResult = await users.AddToRoleAsync(user, Roles.User);   // public sign-up is ALWAYS "User"
        if (!roleResult.Succeeded)
        {
            if (tx is not null)
                await tx.RollbackAsync();
            else
                await users.DeleteAsync(user);

            log.LogWarning("Registration failed assigning role {Role} to user {UserId}", Roles.User, user.Id);
            throw new AppValidationException(string.Join(" ", roleResult.Errors.Select(e => e.Description)));
        }

        if (tx is not null)
            await tx.CommitAsync();

        log.LogInformation("User {UserId} registered with role {Role}", user.Id, Roles.User);

        return new UserInfoDto(user.Id, user.FullName, user.Email!, [Roles.User]);
    }

    private async Task ValidatePasswordAsync(ApplicationUser user, string password)
    {
        if (users.PasswordValidators is null || users.PasswordValidators.Count == 0)
            return;

        var errors = new List<string>();
        foreach (var validator in users.PasswordValidators)
        {
            var res = await validator.ValidateAsync(users, user, password);
            if (!res.Succeeded)
                errors.AddRange(res.Errors.Select(e => e.Description));
        }

        if (errors.Count > 0)
        {
            log.LogWarning("Registration failed password validation with {ErrorCount} error(s)", errors.Count);
            throw new AppValidationException(string.Join(" ", errors));
        }
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

