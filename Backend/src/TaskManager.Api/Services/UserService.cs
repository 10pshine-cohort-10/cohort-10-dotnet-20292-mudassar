using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;

namespace TaskManager.Api.Services;

public interface IUserService
{
    Task<IReadOnlyList<UserDto>> GetAllAsync(string? search = null, bool? activeOnly = null);
    Task<IReadOnlyList<UserLookupDto>> GetLookupAsync();
    Task<UserDto> GetByIdAsync(Guid id);
    Task ChangeRoleAsync(Guid targetUserId, string role, Guid actingAdminId);
    Task SetActiveAsync(Guid targetUserId, bool isActive, Guid actingAdminId);
}

public class UserService(
    UserManager<ApplicationUser> users,
    ILogger<UserService> log,
    AppDbContext? db = null) : IUserService
{
    private static readonly SemaphoreSlim AdminStateLock = new(1, 1);
    public async Task<IReadOnlyList<UserDto>> GetAllAsync(string? search = null, bool? activeOnly = null)
    {
        var query = users.Users.AsQueryable();

        if (activeOnly == true)
            query = query.Where(u => u.IsActive);

        var list = query is IAsyncEnumerable<ApplicationUser>
            ? await query.OrderBy(u => u.FullName).ToListAsync()
            : query.OrderBy(u => u.FullName).ToList();

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            list = list
                .Where(u => u.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || (u.Email is not null && u.Email.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        var result = new List<UserDto>(list.Count);
        if (db is not null && list.Count > 0)
        {
            var userIds = list.Select(u => u.Id).ToList();
            var roleRows = await (
                from ur in db.UserRoles.AsNoTracking()
                join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where userIds.Contains(ur.UserId) && r.Name != null
                select new { ur.UserId, RoleName = r.Name! }
            ).ToListAsync();

            var rolesByUser = roleRows
                .GroupBy(x => x.UserId)
                .ToDictionary(g => g.Key, g => (IList<string>)g.Select(x => x.RoleName).ToList());

            foreach (var u in list)
            {
                var roles = rolesByUser.TryGetValue(u.Id, out var r) ? r : new List<string>();
                result.Add(new UserDto(u.Id, u.FullName, u.Email!, roles, u.IsActive, u.CreatedAt));
            }
        }
        else
        {
            foreach (var u in list)
            {
                var roles = await users.GetRolesAsync(u);
                result.Add(new UserDto(u.Id, u.FullName, u.Email!, roles, u.IsActive, u.CreatedAt));
            }
        }

        return result;
    }

    public async Task<IReadOnlyList<UserLookupDto>> GetLookupAsync()
    {
        var query = users.Users.Where(u => u.IsActive).OrderBy(u => u.FullName);

        var activeUsers = query is IAsyncEnumerable<ApplicationUser>
            ? await query.Select(u => new UserLookupDto(u.Id, u.FullName)).ToListAsync()
            : query.Select(u => new UserLookupDto(u.Id, u.FullName)).ToList();

        return activeUsers;
    }

    public async Task<UserDto> GetByIdAsync(Guid id)
    {
        var user = await users.FindByIdAsync(id.ToString())
            ?? throw new NotFoundException("User was not found.");

        var roles = await users.GetRolesAsync(user);
        return new UserDto(user.Id, user.FullName, user.Email!, roles, user.IsActive, user.CreatedAt);
    }

    public async Task ChangeRoleAsync(Guid targetUserId, string role, Guid actingAdminId)
    {
        var normalizedRole = Roles.All.FirstOrDefault(r =>
            string.Equals(r, role?.Trim(), StringComparison.OrdinalIgnoreCase));

        if (normalizedRole is null)
            throw new AppValidationException($"Invalid role '{role}'. Allowed roles: {string.Join(", ", Roles.All)}.");

        if (targetUserId == actingAdminId && normalizedRole != Roles.Admin)
            throw new AppValidationException("You cannot demote yourself.");

        await AdminStateLock.WaitAsync();
        try
        {
            await using var tx = db is not null && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;

            var user = await users.FindByIdAsync(targetUserId.ToString())
                ?? throw new NotFoundException("User was not found.");

            var currentRoles = await users.GetRolesAsync(user) ?? [];

            // No-op if the user already holds solely the target role (avoids unnecessary remove/add window).
            if (currentRoles.Count == 1 && string.Equals(currentRoles[0], normalizedRole, StringComparison.Ordinal))
            {
                if (tx is not null)
                    await tx.CommitAsync();
                return;
            }

            if (currentRoles.Contains(Roles.Admin) && normalizedRole != Roles.Admin && !await HasAnotherActiveAdminAsync(user.Id))
                throw new AppValidationException("Cannot demote the last active admin.");

            if (currentRoles.Count > 0)
            {
                var removeResult = await users.RemoveFromRolesAsync(user, currentRoles);
                if (!removeResult.Succeeded)
                    throw new AppValidationException(string.Join(" ", removeResult.Errors.Select(e => e.Description)));
            }

            var addResult = await users.AddToRoleAsync(user, normalizedRole);
            if (!addResult.Succeeded)
                throw new AppValidationException(string.Join(" ", addResult.Errors.Select(e => e.Description)));

            // Revoke previously issued JWT tokens so role changes take effect immediately.
            await users.UpdateSecurityStampAsync(user);

            if (tx is not null)
                await tx.CommitAsync();

            log.LogWarning(
                "Admin {AdminId} changed role of user {TargetUserId} from [{OldRoles}] to {NewRole}",
                actingAdminId, user.Id, string.Join(", ", currentRoles), normalizedRole);
        }
        finally
        {
            AdminStateLock.Release();
        }
    }

    public async Task SetActiveAsync(Guid targetUserId, bool isActive, Guid actingAdminId)
    {
        if (!isActive && targetUserId == actingAdminId)
            throw new AppValidationException("You cannot deactivate yourself.");

        await AdminStateLock.WaitAsync();
        try
        {
            await using var tx = db is not null && db.Database.IsRelational()
                ? await db.Database.BeginTransactionAsync(IsolationLevel.Serializable)
                : null;

            var user = await users.FindByIdAsync(targetUserId.ToString())
                ?? throw new NotFoundException("User was not found.");

            if (!isActive)
            {
                var currentRoles = await users.GetRolesAsync(user) ?? [];
                if (currentRoles.Contains(Roles.Admin) && !await HasAnotherActiveAdminAsync(user.Id))
                    throw new AppValidationException("Cannot deactivate the last active admin.");
            }

            user.IsActive = isActive;
            var updateResult = await users.UpdateAsync(user);
            if (!updateResult.Succeeded)
                throw new AppValidationException(string.Join(" ", updateResult.Errors.Select(e => e.Description)));

            if (!isActive)
                await users.UpdateSecurityStampAsync(user);

            if (tx is not null)
                await tx.CommitAsync();

            log.LogWarning(
                "Admin {AdminId} set user {TargetUserId} active={IsActive}",
                actingAdminId, user.Id, isActive);
        }
        finally
        {
            AdminStateLock.Release();
        }
    }

    private async Task<bool> HasAnotherActiveAdminAsync(Guid excludedUserId)
    {
        if (db is not null)
        {
            return await (
                from u in db.Users.AsNoTracking()
                join ur in db.UserRoles.AsNoTracking() on u.Id equals ur.UserId
                join r in db.Roles.AsNoTracking() on ur.RoleId equals r.Id
                where u.IsActive && u.Id != excludedUserId && r.Name == Roles.Admin
                select u.Id
            ).AnyAsync();
        }

        var admins = await users.GetUsersInRoleAsync(Roles.Admin);
        return admins is null || admins.Any(a => a.IsActive && a.Id != excludedUserId);
    }
}

