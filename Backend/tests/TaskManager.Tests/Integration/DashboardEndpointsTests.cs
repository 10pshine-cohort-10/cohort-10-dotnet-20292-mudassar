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
        var token = (await login.Content.ReadFromJsonAsync<AuthResponse>())!.Token;

        HttpRequestMessage Req(HttpMethod m, string url, object? body = null)
        {
            var r = new HttpRequestMessage(m, url);
            r.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            if (body is not null) r.Content = JsonContent.Create(body);
            return r;
        }

        var ids = new List<int>();
        for (var i = 0; i < 3; i++)
        {
            var created = await (await _client.SendAsync(Req(HttpMethod.Post, "/api/tasks", new CreateTaskDto($"Task {i}"))))
                .Content.ReadFromJsonAsync<TaskDto>();
            ids.Add(created!.Id);
        }

        (await _client.SendAsync(Req(HttpMethod.Patch, $"/api/tasks/{ids[0]}/status",
            new UpdateTaskStatusDto(TaskItemStatus.Completed)))).EnsureSuccessStatusCode();
        (await _client.SendAsync(Req(HttpMethod.Patch, $"/api/tasks/{ids[1]}/status",
            new UpdateTaskStatusDto(TaskItemStatus.InProgress)))).EnsureSuccessStatusCode();
        (await _client.SendAsync(Req(HttpMethod.Delete, $"/api/tasks/{ids[2]}"))).EnsureSuccessStatusCode();

        var res = await _client.SendAsync(Req(HttpMethod.Get, "/api/dashboard/summary"));
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var summary = await res.Content.ReadFromJsonAsync<DashboardSummaryDto>();

        Assert.Equal(new DashboardSummaryDto(Completed: 1, InProgress: 1, Pending: 0, Total: 2), summary);
    }
}

