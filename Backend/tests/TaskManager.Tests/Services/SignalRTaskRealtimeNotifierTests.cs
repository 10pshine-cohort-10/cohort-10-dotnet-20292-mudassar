using System.Security.Claims;
using Microsoft.AspNetCore.SignalR;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Hubs;
using TaskManager.Api.Services;

namespace TaskManager.Tests.Services;

public sealed class SignalRTaskRealtimeNotifierTests
{
    private static TaskStatusUpdatedEvent SampleEvent(Guid createdById, Guid? assignedToId) => new(
        TaskId: 7,
        Title: "Ship SignalR",
        OldStatus: TaskItemStatus.Pending,
        NewStatus: TaskItemStatus.InProgress,
        UpdatedAt: DateTime.UtcNow,
        RowVersion: "AQIDBA==",
        UpdatedById: createdById,
        CreatedById: createdById,
        AssignedToId: assignedToId);

    [Fact]
    public async Task NotifyTaskStatusUpdated_TargetsAdminsCreatorAndDistinctAssignee()
    {
        var creatorId = Guid.NewGuid();
        var assigneeId = Guid.NewGuid();
        IReadOnlyList<string>? capturedGroups = null;

        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(c => c.SendCoreAsync(
                TaskHub.TaskStatusUpdatedMethod,
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients
            .Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(g => capturedGroups = g)
            .Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<TaskHub>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

        var sut = new SignalRTaskRealtimeNotifier(
            hubContext.Object,
            NullLogger<SignalRTaskRealtimeNotifier>.Instance);

        var evt = SampleEvent(creatorId, assigneeId);
        await sut.NotifyTaskStatusUpdatedAsync(evt);

        Assert.NotNull(capturedGroups);
        Assert.Equal(3, capturedGroups.Count);
        Assert.Contains(TaskHub.AdminsGroup, capturedGroups);
        Assert.Contains(TaskHub.UserGroup(creatorId), capturedGroups);
        Assert.Contains(TaskHub.UserGroup(assigneeId), capturedGroups);

        clientProxy.Verify(
            c => c.SendCoreAsync(
                TaskHub.TaskStatusUpdatedMethod,
                It.Is<object?[]>(args => args.Length == 1 && ReferenceEquals(args[0], evt)),
                It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public async Task NotifyTaskStatusUpdated_CreatorEqualsAssignee_DeduplicatesUserGroup()
    {
        var userId = Guid.NewGuid();
        IReadOnlyList<string>? capturedGroups = null;

        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .Returns(Task.CompletedTask);

        var clients = new Mock<IHubClients>();
        clients
            .Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>()))
            .Callback<IReadOnlyList<string>>(g => capturedGroups = g)
            .Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<TaskHub>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

        var sut = new SignalRTaskRealtimeNotifier(
            hubContext.Object,
            NullLogger<SignalRTaskRealtimeNotifier>.Instance);

        await sut.NotifyTaskStatusUpdatedAsync(SampleEvent(userId, userId));

        Assert.NotNull(capturedGroups);
        Assert.Equal(2, capturedGroups.Count);
        Assert.Contains(TaskHub.AdminsGroup, capturedGroups);
        Assert.Contains(TaskHub.UserGroup(userId), capturedGroups);
    }

    [Fact]
    public async Task NotifyTaskStatusUpdated_HubSendThrows_SwallowsAndDoesNotFailCaller()
    {
        var clientProxy = new Mock<IClientProxy>();
        clientProxy
            .Setup(c => c.SendCoreAsync(
                It.IsAny<string>(),
                It.IsAny<object?[]>(),
                It.IsAny<CancellationToken>()))
            .ThrowsAsync(new InvalidOperationException("Hub transport failure"));

        var clients = new Mock<IHubClients>();
        clients.Setup(c => c.Groups(It.IsAny<IReadOnlyList<string>>())).Returns(clientProxy.Object);

        var hubContext = new Mock<IHubContext<TaskHub>>();
        hubContext.SetupGet(h => h.Clients).Returns(clients.Object);

        var sut = new SignalRTaskRealtimeNotifier(
            hubContext.Object,
            NullLogger<SignalRTaskRealtimeNotifier>.Instance);

        var exception = await Record.ExceptionAsync(() =>
            sut.NotifyTaskStatusUpdatedAsync(SampleEvent(Guid.NewGuid(), null)));

        Assert.Null(exception);
    }

    [Fact]
    public async Task TaskHub_OnConnected_RegularUserJoinsUserGroupOnly_AdminJoinsUserAndAdminsGroups()
    {
        var regularUserId = Guid.NewGuid();
        var regularGroups = new Mock<IGroupManager>();
        var regularContext = new Mock<HubCallerContext>();
        regularContext.SetupGet(c => c.ConnectionId).Returns("conn-user");
        regularContext.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);
        regularContext.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, regularUserId.ToString()),
            new Claim(ClaimTypes.Role, Roles.User)
        ], "Jwt")));

        using var regularHub = new TaskHub
        {
            Context = regularContext.Object,
            Groups = regularGroups.Object
        };
        await regularHub.OnConnectedAsync();

        regularGroups.Verify(
            g => g.AddToGroupAsync("conn-user", TaskHub.UserGroup(regularUserId), It.IsAny<CancellationToken>()),
            Times.Once);
        regularGroups.Verify(
            g => g.AddToGroupAsync("conn-user", TaskHub.AdminsGroup, It.IsAny<CancellationToken>()),
            Times.Never);

        var adminUserId = Guid.NewGuid();
        var adminGroups = new Mock<IGroupManager>();
        var adminContext = new Mock<HubCallerContext>();
        adminContext.SetupGet(c => c.ConnectionId).Returns("conn-admin");
        adminContext.SetupGet(c => c.ConnectionAborted).Returns(CancellationToken.None);
        adminContext.SetupGet(c => c.User).Returns(new ClaimsPrincipal(new ClaimsIdentity(
        [
            new Claim(ClaimTypes.NameIdentifier, adminUserId.ToString()),
            new Claim(ClaimTypes.Role, Roles.Admin)
        ], "Jwt")));

        using var adminHub = new TaskHub
        {
            Context = adminContext.Object,
            Groups = adminGroups.Object
        };
        await adminHub.OnConnectedAsync();

        adminGroups.Verify(
            g => g.AddToGroupAsync("conn-admin", TaskHub.UserGroup(adminUserId), It.IsAny<CancellationToken>()),
            Times.Once);
        adminGroups.Verify(
            g => g.AddToGroupAsync("conn-admin", TaskHub.AdminsGroup, It.IsAny<CancellationToken>()),
            Times.Once);
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        var hubContext = new Mock<IHubContext<TaskHub>>().Object;
        Assert.Throws<ArgumentNullException>(() =>
            new SignalRTaskRealtimeNotifier(null!, NullLogger<SignalRTaskRealtimeNotifier>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new SignalRTaskRealtimeNotifier(hubContext, null!));
    }
}

