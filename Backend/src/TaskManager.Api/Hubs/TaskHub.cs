using System.Collections.Concurrent;
using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskManager.Api.Extensions;

namespace TaskManager.Api.Hubs;

public interface ITaskHubConnectionTracker
{
    void Add(Guid userId, string connectionId);
    void Remove(Guid userId, string connectionId);
    IReadOnlyCollection<string> GetConnections(Guid userId);
}

public sealed class NullTaskHubConnectionTracker : ITaskHubConnectionTracker
{
    public static readonly NullTaskHubConnectionTracker Instance = new();

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
}

public sealed class TaskHubConnectionTracker : ITaskHubConnectionTracker
{
    private readonly ConcurrentDictionary<Guid, ConcurrentDictionary<string, byte>> connections = new();

    public void Add(Guid userId, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return;

        var userConnections = connections.GetOrAdd(userId, _ => new ConcurrentDictionary<string, byte>(StringComparer.Ordinal));
        userConnections[connectionId] = 0;
    }

    public void Remove(Guid userId, string connectionId)
    {
        if (string.IsNullOrEmpty(connectionId))
            return;

        if (connections.TryGetValue(userId, out var userConnections))
        {
            userConnections.TryRemove(connectionId, out _);
            if (userConnections.IsEmpty)
                connections.TryRemove(new KeyValuePair<Guid, ConcurrentDictionary<string, byte>>(userId, userConnections));
        }
    }

    public IReadOnlyCollection<string> GetConnections(Guid userId) =>
        connections.TryGetValue(userId, out var userConnections)
            ? userConnections.Keys.ToArray()
            : [];
}

[Authorize]
public class TaskHub : Hub
{
    public const string HubPath = "/hubs/tasks";
    public const string AdminsGroup = "admins";
    public const string TaskStatusUpdatedMethod = "TaskStatusUpdated";

    private readonly ITaskHubConnectionTracker connectionTracker;

    public TaskHub(ITaskHubConnectionTracker? connectionTracker = null)
    {
        this.connectionTracker = connectionTracker ?? NullTaskHubConnectionTracker.Instance;
    }

    public static string UserGroup(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is not null)
        {
            var userId = user.GetUserId();
            connectionTracker.Add(userId, Context.ConnectionId);
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), Context.ConnectionAborted);

            if (user.IsAdmin())
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
