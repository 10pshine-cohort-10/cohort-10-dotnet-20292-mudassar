using System.Diagnostics.CodeAnalysis;
using Microsoft.AspNetCore.SignalR;
using TaskManager.Api.DTOs;
using TaskManager.Api.Hubs;

namespace TaskManager.Api.Services;

public interface ITaskRealtimeNotifier
{
    Task NotifyTaskStatusUpdatedAsync(
        TaskStatusUpdatedEvent update,
        CancellationToken cancellationToken = default);

    Task RemoveUserFromAdminGroupAsync(
        Guid userId,
        CancellationToken cancellationToken = default);

    Task RevokeUserConnectionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class NullTaskRealtimeNotifier : ITaskRealtimeNotifier
{
    public static readonly NullTaskRealtimeNotifier Instance = new();

    private NullTaskRealtimeNotifier()
    {
    }

    public Task NotifyTaskStatusUpdatedAsync(
        TaskStatusUpdatedEvent update,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RemoveUserFromAdminGroupAsync(
        Guid userId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;

    public Task RevokeUserConnectionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) => Task.CompletedTask;
}

public sealed class SignalRTaskRealtimeNotifier : ITaskRealtimeNotifier
{
    private readonly IHubContext<TaskHub> hubContext;
    private readonly ITaskHubConnectionTracker connectionTracker;
    private readonly ILogger<SignalRTaskRealtimeNotifier> log;

    public SignalRTaskRealtimeNotifier(
        IHubContext<TaskHub> hubContext,
        ILogger<SignalRTaskRealtimeNotifier> log,
        ITaskHubConnectionTracker? connectionTracker = null)
    {
        ArgumentNullException.ThrowIfNull(hubContext);
        ArgumentNullException.ThrowIfNull(log);
        this.hubContext = hubContext;
        this.log = log;
        this.connectionTracker = connectionTracker ?? NullTaskHubConnectionTracker.Instance;
    }

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Realtime broadcast failures must not fail an already-committed task status update.")]
    public async Task NotifyTaskStatusUpdatedAsync(
        TaskStatusUpdatedEvent update,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(update);

        var targetGroups = new List<string>(3)
        {
            TaskHub.AdminsGroup,
            TaskHub.UserGroup(update.CreatedById)
        };

        if (update.AssignedToId.HasValue && update.AssignedToId.Value != update.CreatedById)
            targetGroups.Add(TaskHub.UserGroup(update.AssignedToId.Value));

        try
        {
            await hubContext.Clients
                .Groups(targetGroups)
                .SendAsync(TaskHub.TaskStatusUpdatedMethod, update, cancellationToken);
        }
        catch (Exception ex) when (ex is not OperationCanceledException)
        {
            log.LogWarning(
                ex,
                "Failed to broadcast real-time status update for task {TaskId} ({OldStatus} -> {NewStatus})",
                update.TaskId,
                update.OldStatus,
                update.NewStatus);
        }
    }

    public Task RemoveUserFromAdminGroupAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        RemoveConnectionsFromGroupsAsync(userId, [TaskHub.AdminsGroup], cancellationToken);

    public Task RevokeUserConnectionsAsync(
        Guid userId,
        CancellationToken cancellationToken = default) =>
        RemoveConnectionsFromGroupsAsync(userId, [TaskHub.AdminsGroup, TaskHub.UserGroup(userId)], cancellationToken);

    [SuppressMessage("Design", "CA1031:Do not catch general exception types", Justification = "Group removal failures must not fail already-committed user role or status updates.")]
    private async Task RemoveConnectionsFromGroupsAsync(
        Guid userId,
        string[] groupNames,
        CancellationToken cancellationToken)
    {
        var connectionIds = connectionTracker.GetConnections(userId);
        foreach (var connectionId in connectionIds)
        {
            foreach (var groupName in groupNames)
            {
                try
                {
                    await hubContext.Groups.RemoveFromGroupAsync(connectionId, groupName, cancellationToken);
                }
                catch (Exception ex) when (ex is not OperationCanceledException)
                {
                    log.LogWarning(
                        ex,
                        "Failed to remove connection {ConnectionId} for user {UserId} from group {GroupName}",
                        connectionId,
                        userId,
                        groupName);
                }
            }
        }
    }
}
