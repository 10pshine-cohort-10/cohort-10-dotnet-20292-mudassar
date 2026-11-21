using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Integration;

public class TasksEndpointsTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<AuthResponse> LoginAdminAsync()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(factory.AdminEmail, factory.AdminPassword));
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth;
    }

    private async Task<AuthResponse> RegisterAndLoginUserAsync(string name = "Task User")
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto(name, email, TestEnv.UserPassword));
        reg.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(email, TestEnv.UserPassword));
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth;
    }

    private static HttpRequestMessage Authorized(HttpMethod method, string url, string token, object? body = null)
    {
        var req = new HttpRequestMessage(method, url);
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        if (body is not null)
            req.Content = JsonContent.Create(body);
        return req;
    }

    [Fact]
    public async Task TaskCrud_OwnershipConcurrencyAndSoftDelete_FullFlow()
    {
        var owner = await RegisterAndLoginUserAsync("Owner");
        var assignee = await RegisterAndLoginUserAsync("Assignee");
        var stranger = await RegisterAndLoginUserAsync("Stranger");
        var admin = await LoginAdminAsync();

        // 1. Create with past due date -> 400 BadRequest
        using var badCreateReq = Authorized(
            HttpMethod.Post,
            "/api/tasks",
            owner.Token,
            new CreateTaskDto("Invalid Due Date", DueDate: DateTime.UtcNow.Date.AddDays(-2)));
        var badCreateRes = await _client.SendAsync(badCreateReq);
        Assert.Equal(HttpStatusCode.BadRequest, badCreateRes.StatusCode);

        // 2. Valid Create -> 201 Created + Location
        using var createReq = Authorized(
            HttpMethod.Post,
            "/api/tasks",
            owner.Token,
            new CreateTaskDto(
                Title: "Launch API",
                Description: "Ship Branch 6",
                Priority: TaskPriority.High,
                DueDate: DateTime.UtcNow.Date.AddDays(3),
                AssignedToId: assignee.User.Id));
        var createRes = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        Assert.NotNull(createRes.Headers.Location);
        var created = await createRes.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(created);
        Assert.Equal("Owner", created.CreatedByName);
        Assert.Equal("Assignee", created.AssignedToName);

        // 3. Stranger GetById -> 403 Forbidden
        using var strangerGetReq = Authorized(HttpMethod.Get, $"/api/tasks/{created.Id}", stranger.Token);
        var strangerGetRes = await _client.SendAsync(strangerGetReq);
        Assert.Equal(HttpStatusCode.Forbidden, strangerGetRes.StatusCode);

        // 4. Assignee PATCH status -> 200 OK
        using var patchReq = Authorized(
            HttpMethod.Patch,
            $"/api/tasks/{created.Id}/status",
            assignee.Token,
            new UpdateTaskStatusDto(TaskItemStatus.InProgress));
        var patchRes = await _client.SendAsync(patchReq);
        Assert.Equal(HttpStatusCode.OK, patchRes.StatusCode);
        var afterPatch = await patchRes.Content.ReadFromJsonAsync<TaskDto>();
        Assert.NotNull(afterPatch);
        Assert.Equal(TaskItemStatus.InProgress, afterPatch.Status);

        // 5. Optimistic concurrency: stale RowVersion on PUT -> 409 Conflict
        using var stalePutReq = Authorized(
            HttpMethod.Put,
            $"/api/tasks/{created.Id}",
            owner.Token,
            new UpdateTaskDto(
                Title: "Stale Update",
                Status: TaskItemStatus.Completed,
                Priority: TaskPriority.Critical,
                AssignedToId: assignee.User.Id,
                RowVersion: created.RowVersion)); // stale! afterPatch changed RowVersion
        var stalePutRes = await _client.SendAsync(stalePutReq);
        Assert.Equal(HttpStatusCode.Conflict, stalePutRes.StatusCode);

        // 6. Fresh RowVersion on PUT -> 200 OK
        using var freshPutReq = Authorized(
            HttpMethod.Put,
            $"/api/tasks/{created.Id}",
            owner.Token,
            new UpdateTaskDto(
                Title: "Launch API v2",
                Description: "Updated",
                Status: TaskItemStatus.Completed,
                Priority: TaskPriority.Critical,
                AssignedToId: assignee.User.Id,
                RowVersion: afterPatch.RowVersion));
        var freshPutRes = await _client.SendAsync(freshPutReq);
        Assert.Equal(HttpStatusCode.OK, freshPutRes.StatusCode);

        // 7. Assignee cannot DELETE -> 403 Forbidden
        using var assigneeDeleteReq = Authorized(HttpMethod.Delete, $"/api/tasks/{created.Id}", assignee.Token);
        var assigneeDeleteRes = await _client.SendAsync(assigneeDeleteReq);
        Assert.Equal(HttpStatusCode.Forbidden, assigneeDeleteRes.StatusCode);

        // 8. Owner DELETE (soft delete) -> 204 NoContent
        using var ownerDeleteReq = Authorized(HttpMethod.Delete, $"/api/tasks/{created.Id}", owner.Token);
        var ownerDeleteRes = await _client.SendAsync(ownerDeleteReq);
        Assert.Equal(HttpStatusCode.NoContent, ownerDeleteRes.StatusCode);

        // 9. Soft-deleted task disappears from GetById (404) and list
        using var getDeletedReq = Authorized(HttpMethod.Get, $"/api/tasks/{created.Id}", admin.Token);
        var getDeletedRes = await _client.SendAsync(getDeletedReq);
        Assert.Equal(HttpStatusCode.NotFound, getDeletedRes.StatusCode);

        using var listReq = Authorized(HttpMethod.Get, "/api/tasks", owner.Token);
        var listRes = await _client.SendAsync(listReq);
        var page = await listRes.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(page);
        Assert.DoesNotContain(page.Items, t => t.Id == created.Id);
    }

    [Fact]
    public async Task GetTasks_SupportsQueryFilters()
    {
        var owner = await RegisterAndLoginUserAsync("FilterOwner");
        var uniqueTitle = $"FilterTarget-{Guid.NewGuid():N}"[..20];

        using var createReq = Authorized(
            HttpMethod.Post,
            "/api/tasks",
            owner.Token,
            new CreateTaskDto(uniqueTitle, Priority: TaskPriority.Critical));
        var createRes = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);

        using var filterReq = Authorized(
            HttpMethod.Get,
            $"/api/tasks?priority=Critical&search={uniqueTitle}",
            owner.Token);
        var filterRes = await _client.SendAsync(filterReq);
        Assert.Equal(HttpStatusCode.OK, filterRes.StatusCode);

        var page = await filterRes.Content.ReadFromJsonAsync<PagedResult<TaskDto>>();
        Assert.NotNull(page);
        Assert.Single(page.Items);
        Assert.Equal(uniqueTitle, page.Items[0].Title);
    }
}

