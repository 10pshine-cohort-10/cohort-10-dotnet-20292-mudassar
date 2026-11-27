using System.Data;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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

public class UserService : IUserService
{
    private static readonly SemaphoreSlim AdminStateLock = new(1, 1);
    private readonly UserManager<ApplicationUser> users;
    private readonly ILogger<UserService> log;
    private readonly AppDbContext? db;
    private readonly ITaskRealtimeNotifier realtimeNotifier;

    public UserService(
        UserManager<ApplicationUser> users,
        ILogger<UserService> log,
        AppDbContext? db = null,
        ITaskRealtimeNotifier? realtimeNotifier = null)
    {
        ArgumentNullException.ThrowIfNull(users);
        ArgumentNullException.ThrowIfNull(log);
        this.users = users;
        this.log = log;
        this.db = db;
        this.realtimeNotifier = realtimeNotifier ?? NullTaskRealtimeNotifier.Instance;
    }
    public async Task<IReadOnlyList<UserDto>> GetAllAsync(string? search = null, bool? activeOnly = null)
    {
        var query = users.Users.AsQueryable();

        if (activeOnly == true)
            query = query.Where(u => u.IsActive);

        var list = await MaterializeAsync(query.OrderBy(u => u.FullName));

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim();
            list = list
                .Where(u => u.FullName.Contains(term, StringComparison.OrdinalIgnoreCase)
                         || (u.Email is not null && u.Email.Contains(term, StringComparison.OrdinalIgnoreCase)))
                .ToList();
        }

        return await MapUsersWithRolesAsync(list);
    }

    private async Task<IReadOnlyList<UserDto>> MapUsersWithRolesAsync(List<ApplicationUser> list)
    {
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
            return result;
        }

        foreach (var u in list)
        {
            var roles = await users.GetRolesAsync(u);
            result.Add(new UserDto(u.Id, u.FullName, u.Email!, roles, u.IsActive, u.CreatedAt));
        }

        return result;
    }

    public async Task<IReadOnlyList<UserLookupDto>> GetLookupAsync()
    {
        var query = users.Users
            .Where(u => u.IsActive)
            .OrderBy(u => u.FullName)
            .Select(u => new UserLookupDto(u.Id, u.FullName));

        return await MaterializeAsync(query);
    }

    private static Task<List<T>> MaterializeAsync<T>(IQueryable<T> query) =>
        query is IAsyncEnumerable<T>
            ? query.ToListAsync()
            : Task.FromResult(query.AsEnumerable().ToList());

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
            await using var tx = await BeginSerializableTransactionAsync();

            var user = await users.FindByIdAsync(targetUserId.ToString())
                ?? throw new NotFoundException("User was not found.");

            var currentRoles = await users.GetRolesAsync(user) ?? [];
            if (HasOnlyRole(currentRoles, normalizedRole))
            {
                await CommitIfPresentAsync(tx);
                return;
            }

            var demotingAdmin = IsDemotingAdmin(currentRoles, normalizedRole);
            if (demotingAdmin && !await HasAnotherActiveAdminAsync(user.Id))
                throw new AppValidationException("Cannot demote the last active admin.");

            await ReplaceUserRolesAndStampAsync(user, currentRoles, normalizedRole);
            await CommitIfPresentAsync(tx);

            if (demotingAdmin)
                await realtimeNotifier.RemoveUserFromAdminGroupAsync(user.Id);

            log.LogWarning(
                "Admin {AdminId} changed role of user {TargetUserId} from [{OldRoles}] to {NewRole}",
                actingAdminId, user.Id, string.Join(", ", currentRoles), normalizedRole);
        }
        finally
        {
            AdminStateLock.Release();
        }
    }

    private Task<IDbContextTransaction?> BeginSerializableTransactionAsync() =>
        db is not null && db.Database.IsRelational()
            ? BeginSerializableTransactionCoreAsync(db)
            : Task.FromResult<IDbContextTransaction?>(null);

    private static async Task<IDbContextTransaction?> BeginSerializableTransactionCoreAsync(AppDbContext context) =>
        await context.Database.BeginTransactionAsync(IsolationLevel.Serializable);

    private static Task CommitIfPresentAsync(IDbContextTransaction? tx) =>
        tx is not null ? tx.CommitAsync() : Task.CompletedTask;

    private static bool HasOnlyRole(IList<string> currentRoles, string normalizedRole) =>
        currentRoles.Count == 1 && string.Equals(currentRoles[0], normalizedRole, StringComparison.Ordinal);

    private static bool IsDemotingAdmin(IList<string> currentRoles, string normalizedRole) =>
        currentRoles.Contains(Roles.Admin) && normalizedRole != Roles.Admin;

    private async Task ReplaceUserRolesAndStampAsync(
        ApplicationUser user,
        IList<string> currentRoles,
        string normalizedRole)
    {
        if (currentRoles.Count > 0)
            EnsureIdentitySucceeded(await users.RemoveFromRolesAsync(user, currentRoles));

        EnsureIdentitySucceeded(await users.AddToRoleAsync(user, normalizedRole));

        // Revoke previously issued JWT tokens so role changes take effect immediately.
        EnsureIdentitySucceeded(await users.UpdateSecurityStampAsync(user));
    }

    private static void EnsureIdentitySucceeded(IdentityResult result)
    {
        if (!result.Succeeded)
            throw new AppValidationException(string.Join(" ", result.Errors.Select(e => e.Description)));
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
            {
                var stampResult = await users.UpdateSecurityStampAsync(user);
                if (!stampResult.Succeeded)
                    throw new AppValidationException(string.Join(" ", stampResult.Errors.Select(e => e.Description)));
            }

            if (tx is not null)
                await tx.CommitAsync();

            if (!isActive)
                await realtimeNotifier.RevokeUserConnectionsAsync(user.Id);

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

