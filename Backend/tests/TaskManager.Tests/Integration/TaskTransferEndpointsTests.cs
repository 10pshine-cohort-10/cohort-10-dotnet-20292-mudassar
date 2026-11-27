using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using System.Text;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Integration;

public sealed class TaskTransferEndpointsTests : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client;

    public TaskTransferEndpointsTests(TestApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndGetTokenAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@test.local";
        var reg = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterDto("Transfer User", email, TestEnv.UserPassword));
        reg.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync(
            "/api/auth/login",
            new LoginDto(email, TestEnv.UserPassword));
        login.EnsureSuccessStatusCode();
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth.Token;
    }

    [Fact]
    public async Task ExportAndImport_WithoutToken_Return401Unauthorized()
    {
        using var exportRes = await _client.GetAsync("/api/tasks/export?format=json");
        Assert.Equal(HttpStatusCode.Unauthorized, exportRes.StatusCode);

        using var form = new MultipartFormDataContent();
        form.Add(new ByteArrayContent(Encoding.UTF8.GetBytes("[]")), "file", "tasks.json");
        using var importRes = await _client.PostAsync("/api/tasks/import", form);
        Assert.Equal(HttpStatusCode.Unauthorized, importRes.StatusCode);
    }

    [Fact]
    public async Task ExportJsonAndCsv_ThenImportMultipart_RoundTripsTasks()
    {
        var token = await RegisterAndGetTokenAsync("transfer-roundtrip");

        using (var createReq = new HttpRequestMessage(HttpMethod.Post, "/api/tasks"))
        {
            createReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            createReq.Content = JsonContent.Create(new CreateTaskDto(
                Title: "Integration Export Task",
                Description: "Export/import test description",
                Priority: TaskPriority.High));
            using var createRes = await _client.SendAsync(createReq);
            Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        }

        byte[] csvBytes;
        using (var csvReq = new HttpRequestMessage(HttpMethod.Get, "/api/tasks/export?format=csv"))
        {
            csvReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var csvRes = await _client.SendAsync(csvReq);
            Assert.Equal(HttpStatusCode.OK, csvRes.StatusCode);
            Assert.Equal("text/csv", csvRes.Content.Headers.ContentType?.MediaType);
            Assert.Contains(".csv", csvRes.Content.Headers.ContentDisposition?.FileName ?? string.Empty, StringComparison.Ordinal);
            csvBytes = await csvRes.Content.ReadAsByteArrayAsync();
            Assert.Contains("Integration Export Task", Encoding.UTF8.GetString(csvBytes), StringComparison.Ordinal);
        }

        using (var jsonReq = new HttpRequestMessage(HttpMethod.Get, "/api/tasks/export?format=json"))
        {
            jsonReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
            using var jsonRes = await _client.SendAsync(jsonReq);
            Assert.Equal(HttpStatusCode.OK, jsonRes.StatusCode);
            Assert.Equal("application/json", jsonRes.Content.Headers.ContentType?.MediaType);
            var jsonBody = await jsonRes.Content.ReadAsStringAsync();
            Assert.Contains("Integration Export Task", jsonBody, StringComparison.Ordinal);
        }

        using var form = new MultipartFormDataContent();
        var fileContent = new ByteArrayContent(csvBytes);
        fileContent.Headers.ContentType = new MediaTypeHeaderValue("text/csv");
        form.Add(fileContent, "file", "tasks-export.csv");

        using var importReq = new HttpRequestMessage(HttpMethod.Post, "/api/tasks/import");
        importReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", token);
        importReq.Content = form;

        using var importRes = await _client.SendAsync(importReq);
        Assert.Equal(HttpStatusCode.OK, importRes.StatusCode);

        var result = await importRes.Content.ReadFromJsonAsync<TaskImportResultDto>();
        Assert.NotNull(result);
        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(0, result.FailedCount);
        Assert.Empty(result.Errors);
    }
}
