using System.Net;
using System.Net.Http.Json;
using System.Text.Json;
using TaskManager.Api.DTOs;
using TaskManager.Api.Hubs;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Integration;

public sealed class SignalRHubTests : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client;

    public SignalRHubTests(TestApiFactory factory)
    {
        _client = factory.CreateClient();
    }

    private async Task<string> RegisterAndGetTokenAsync(string prefix)
    {
        var email = $"{prefix}-{Guid.NewGuid():N}@test.local";
        var reg = await _client.PostAsJsonAsync(
            "/api/auth/register",
            new RegisterDto("SignalR User", email, TestEnv.UserPassword));
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
    public async Task Negotiate_WithoutToken_Returns401Unauthorized()
    {
        using var response = await _client.PostAsync($"{TaskHub.HubPath}/negotiate?negotiateVersion=1", null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }

    [Fact]
    public async Task Negotiate_WithAccessTokenQueryString_Returns200WithConnectionId()
    {
        var token = await RegisterAndGetTokenAsync("signalr-qs");
        var url = $"{TaskHub.HubPath}/negotiate?negotiateVersion=1&access_token={Uri.EscapeDataString(token)}";

        using var response = await _client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.OK, response.StatusCode);

        using var doc = JsonDocument.Parse(await response.Content.ReadAsStringAsync());
        Assert.True(doc.RootElement.TryGetProperty("connectionId", out var connId));
        Assert.False(string.IsNullOrWhiteSpace(connId.GetString()));
    }

    [Fact]
    public async Task Negotiate_WithInvalidAccessToken_Returns401Unauthorized()
    {
        var url = $"{TaskHub.HubPath}/negotiate?negotiateVersion=1&access_token=not-a-valid-jwt";
        using var response = await _client.PostAsync(url, null);
        Assert.Equal(HttpStatusCode.Unauthorized, response.StatusCode);
    }
}
