using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Integration;

public class DashboardEndpointsTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Summary_WithoutToken_Returns401()
    {
        var res = await _client.GetAsync("/api/dashboard/summary");
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Summary_CountsOnlyOwnTasksByStatus_AndIgnoresDeleted()
    {
        var email = $"dash-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Dash User", email, TestEnv.UserPassword))).EnsureSuccessStatusCode();
        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto(email, TestEnv.UserPassword));
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        var token = auth.Token;

        HttpRequestMessage Req(HttpMethod m, string url, object? body = null)
        {
            var r = new HttpRequestMessage(m, url);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) r.Content = JsonContent.Create(body);
            return r;
        }

        var adminLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(factory.AdminEmail, factory.AdminPassword));
        adminLogin.EnsureSuccessStatusCode();
        var adminAuth = await adminLogin.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(adminAuth);

        using var baselineAdminReq = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/summary");
        baselineAdminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);
        var baselineAdminRes = await _client.SendAsync(baselineAdminReq);
        Assert.Equal(HttpStatusCode.OK, baselineAdminRes.StatusCode);
        var baselineAdminSummary = await baselineAdminRes.Content.ReadFromJsonAsync<DashboardSummaryDto>();
        Assert.NotNull(baselineAdminSummary);

        // Create a second user with their own task (unassigned to first user) to verify ownership scoping
        var otherEmail = $"other-{Guid.NewGuid():N}@example.com";
        (await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Other User", otherEmail, TestEnv.UserPassword))).EnsureSuccessStatusCode();
        var otherLogin = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto(otherEmail, TestEnv.UserPassword));
        otherLogin.EnsureSuccessStatusCode();
        var otherAuth = await otherLogin.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(otherAuth);
        using (var otherTaskReq = new HttpRequestMessage(HttpMethod.Post, "/api/tasks"))
        {
            otherTaskReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", otherAuth.Token);
            otherTaskReq.Content = JsonContent.Create(new CreateTaskDto("Other User Private Task"));
            (await _client.SendAsync(otherTaskReq)).EnsureSuccessStatusCode();
        }

        var ids = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var createdRes = await _client.SendAsync(Req(HttpMethod.Post, "/api/tasks", new CreateTaskDto($"Task {i}")));
            createdRes.EnsureSuccessStatusCode();
            var created = await createdRes.Content.ReadFromJsonAsync<TaskDto>();
            Assert.NotNull(created);
            ids.Add(created.Id);
        }

        (await _client.SendAsync(Req(HttpMethod.Patch, $"/api/tasks/{ids[0]}/status",
            new UpdateTaskStatusDto(TaskItemStatus.Completed)))).EnsureSuccessStatusCode();
        (await _client.SendAsync(Req(HttpMethod.Patch, $"/api/tasks/{ids[1]}/status",
            new UpdateTaskStatusDto(TaskItemStatus.InProgress)))).EnsureSuccessStatusCode();
        (await _client.SendAsync(Req(HttpMethod.Delete, $"/api/tasks/{ids[2]}"))).EnsureSuccessStatusCode();

        var res = await _client.SendAsync(Req(HttpMethod.Get, "/api/dashboard/summary"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var summary = await res.Content.ReadFromJsonAsync<DashboardSummaryDto>();
        Assert.NotNull(summary);

        Assert.Equal(new DashboardSummaryDto(Completed: 1, InProgress: 1, Pending: 0, Total: 2), summary);

        // Admin sees all users' non-deleted tasks across statuses (+1 Completed, +1 InProgress from user 1, +1 Pending from user 2)
        using var adminReq = new HttpRequestMessage(HttpMethod.Get, "/api/dashboard/summary");
        adminReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", adminAuth.Token);
        var adminRes = await _client.SendAsync(adminReq);
        Assert.Equal(HttpStatusCode.OK, adminRes.StatusCode);
        var adminSummary = await adminRes.Content.ReadFromJsonAsync<DashboardSummaryDto>();
        Assert.NotNull(adminSummary);
        Assert.Equal(baselineAdminSummary.Completed + 1, adminSummary.Completed);
        Assert.Equal(baselineAdminSummary.InProgress + 1, adminSummary.InProgress);
        Assert.Equal(baselineAdminSummary.Pending + 1, adminSummary.Pending);
        Assert.Equal(baselineAdminSummary.Total + 3, adminSummary.Total);
    }
}

