using Microsoft.AspNetCore.Authorization;
using Microsoft.AspNetCore.SignalR;
using TaskManager.Api.Extensions;

namespace TaskManager.Api.Hubs;

[Authorize]
public class TaskHub : Hub
{
    public const string HubPath = "/hubs/tasks";
    public const string AdminsGroup = "admins";
    public const string TaskStatusUpdatedMethod = "TaskStatusUpdated";

    public static string UserGroup(Guid userId) => $"user:{userId:D}";

    public override async Task OnConnectedAsync()
    {
        var user = Context.User;
        if (user is not null)
        {
            var userId = user.GetUserId();
            await Groups.AddToGroupAsync(Context.ConnectionId, UserGroup(userId), Context.ConnectionAborted);

            if (user.IsAdmin())
                await Groups.AddToGroupAsync(Context.ConnectionId, AdminsGroup, Context.ConnectionAborted);
        }

        await base.OnConnectedAsync();
    }
}

