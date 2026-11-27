using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.Identity;
using Microsoft.AspNetCore.SignalR;
using TaskManager.Api.Entities;
using TaskManager.Api.Extensions;

namespace TaskManager.Api.Hubs;

public interface ITaskHubConnectionTracker
{
    void Add(Guid userId, string connectionId);
    void Remove(Guid userId, string connectionId);
    IReadOnlyCollection<string> GetConnections(Guid userId);
    Task<IDisposable> AcquireUserLockAsync(Guid userId, CancellationToken cancellationToken = default);
}

public sealed class NullTaskHubConnectionTracker : ITaskHubConnectionTracker
{
    public static readonly NullTaskHubConnectionTracker Instance = new();
    private static readonly IDisposable NoopLease = new NoopDisposable();

    private NullTaskHubConnectionTracker()
    {
    }

    public void Add(Guid userId, string connectionId)
    {
    }

    public void Remove(Guid userId, string connectionId)
    {
    }

    public IReadOnlyCollection<string> GetConnections(Guid userId) => [];

    public Task<IDisposable> AcquireUserLockAsync(Guid userId, CancellationToken cancellationToken = default) =>
        Task.FromResult(NoopLease);

    private sealed class NoopDisposable : IDisposable
    {
        public void Dispose()
        {
        }
    }
}

public sealed class TaskHubConnectionTracker : ITaskHubConnectionTracker
{
    private readonly Lock syncRoot = new();
    private readonly Dictionary<Guid, HashSet<string>> connections = [];
    private readonly Dictionary<Guid, SemaphoreSlim> userLocks = [];

    public void Add(Guid userId, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return;

        lock (syncRoot)
        {
            if (!connections.TryGetValue(userId, out var userConnections))
            {
                userConnections = new HashSet<string>(StringComparer.Ordinal);
                connections[userId] = userConnections;
            }

            userConnections.Add(connectionId);
        }
    }

    public void Remove(Guid userId, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return;

        lock (syncRoot)
        {
            if (!connections.TryGetValue(userId, out var userConnections))
                return;

            userConnections.Remove(connectionId);
            if (userConnections.Count == 0)
                connections.Remove(userId);
        }
    }

    public IReadOnlyCollection<string> GetConnections(Guid userId)
    {
        lock (syncRoot)
        {
            return connections.TryGetValue(userId, out var userConnections)
                ? userConnections.ToArray()
                : [];
        }
    }

    public async Task<IDisposable> AcquireUserLockAsync(Guid userId, CancellationToken cancellationToken = default)
    {
        SemaphoreSlim gate;
        lock (syncRoot)
        {
            if (!userLocks.TryGetValue(userId, out gate!))
            {
                gate = new SemaphoreSlim(1, 1);
                userLocks[userId] = gate;
            }
        }

        await gate.WaitAsync(cancellationToken);
        return new SemaphoreLease(gate);
    }

    private sealed class SemaphoreLease(SemaphoreSlim gate) : IDisposable
    {
        private int disposed;

        public void Dispose()
        {
            if (Interlocked.Exchange(ref disposed, 1) == 0)
                gate.Release();
        }
    }
}

[Authorize]
public class TaskHub : Hub
{
    public const string HubPath = "/hubs/tasks";
    public const string AdminsGroup = "admins";
    public const string TaskStatusUpdatedMethod = "TaskStatusUpdated";

    private readonly ITaskHubConnectionTracker connectionTracker;
    private readonly UserManager<ApplicationUser>? userManager;

    public TaskHub(
        ITaskHubConnectionTracker? connectionTracker = null,
        UserManager<ApplicationUser>? userManager = null)
    {
        this.connectionTracker = connectionTracker ?? NullTaskHubConnectionTracker.Instance;
        this.userManager = userManager;
    }

    public static string UserGroup(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is not null)
        {
            var userId = user.GetUserId();
            using var userLock = await connectionTracker.AcquireUserLockAsync(userId, Context.ConnectionAborted);

            bool isActive = true;
            bool isAdmin;
            if (userManager is not null)
            {
                var dbUser = await userManager.FindByIdAsync(userId.ToString());
                isActive = dbUser is not null && dbUser.IsActive;
                isAdmin = isActive && await userManager.IsInRoleAsync(dbUser!, Roles.Admin);
            }
            else
            {
                isAdmin = user.IsAdmin();
            }

            if (!isActive)
            {
                Context.Abort();
                return;
            }

            connectionTracker.Add(userId, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), Context.ConnectionAborted);

            if (isAdmin)
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }

    public override Task OnDisconnectedAsync(Exception? exception)
    {
        var user = Context.User;
        if (user is not null)
            connectionTracker.Remove(user.GetUserId(), Context.ConnectionId);

        return base.OnDisconnectedAsync(exception);
    }
}
