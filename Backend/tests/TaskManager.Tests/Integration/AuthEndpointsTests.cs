using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using Microsoft.AspNetCore.Mvc;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Integration;

public class AuthEndpointsTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    [Fact]
    public async Task Health_IsAnonymous_Returns200()
    {
        var res = await _client.GetAsync("/health");
        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
    }

    [Fact]
    public async Task Me_WithoutToken_Returns401ProblemDetails()
    {
        var res = await _client.GetAsync("/api/auth/me");

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        Assert.Equal("application/problem+json", res.Content.Headers.ContentType?.MediaType);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal(401, problem!.Status);
    }

    [Fact]
    public async Task Me_WithInvalidToken_Returns401()
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        req.Headers.Authorization = new AuthenticationHeaderValue("Bearer", "not-a-real-token");

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
    }

    [Fact]
    public async Task Register_Login_Me_Logout_HappyPath()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";

        var reg = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto(TestEnv.UserName, email, TestEnv.UserPassword));
        Assert.Equal(HttpStatusCode.Created, reg.StatusCode);
        var registered = await reg.Content.ReadFromJsonAsync<UserInfoDto>();
        Assert.Equal(new[] { Roles.User }, registered!.Roles);

        var login = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto(email, TestEnv.UserPassword));
        Assert.Equal(HttpStatusCode.OK, login.StatusCode);
        var auth = await login.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.False(string.IsNullOrWhiteSpace(auth!.Token));

        using var meReq = new HttpRequestMessage(HttpMethod.Get, "/api/auth/me");
        meReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var me = await _client.SendAsync(meReq);
        Assert.Equal(HttpStatusCode.OK, me.StatusCode);
        var meDto = await me.Content.ReadFromJsonAsync<UserInfoDto>();
        Assert.Equal(email, meDto!.Email);

        using var logoutReq = new HttpRequestMessage(HttpMethod.Post, "/api/auth/logout");
        logoutReq.Headers.Authorization = new AuthenticationHeaderValue("Bearer", auth.Token);
        var logout = await _client.SendAsync(logoutReq);
        Assert.Equal(HttpStatusCode.NoContent, logout.StatusCode);
    }

    [Fact]
    public async Task Register_DuplicateEmail_Returns409()
    {
        var email = $"dup-{Guid.NewGuid():N}@example.com";
        var dto = new RegisterDto("Dup User", email, TestEnv.UserPassword);

        await _client.PostAsJsonAsync("/api/auth/register", dto);
        var second = await _client.PostAsJsonAsync("/api/auth/register", dto);

        Assert.Equal(HttpStatusCode.Conflict, second.StatusCode);
    }

    [Fact]
    public async Task Register_WeakPassword_Returns400()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto("Weak User", $"weak-{Guid.NewGuid():N}@example.com", "weak"));

        Assert.Equal(HttpStatusCode.BadRequest, res.StatusCode);
    }

    [Fact]
    public async Task Login_WrongPassword_Returns401()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(factory.AdminEmail, $"{factory.AdminPassword}-wrong"));

        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Equal("Invalid email or password.", problem!.Title);
    }

    [Fact]
    public async Task SeededAdmin_CanLogin_AndHasAdminRole()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(factory.AdminEmail, factory.AdminPassword));

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.Contains(Roles.Admin, auth!.User.Roles);
    }

    [Fact]
    public async Task FiveWrongPasswords_LocksAccount()
    {
        var email = $"lock-{Guid.NewGuid():N}@example.com";
        await _client.PostAsJsonAsync("/api/auth/register", new RegisterDto("Lock User", email, TestEnv.UserPassword));

        for (var i = 0; i < 5; i++)
            await _client.PostAsJsonAsync("/api/auth/login", new LoginDto(email, $"{TestEnv.UserPassword}-wrong"));

        var res = await _client.PostAsJsonAsync("/api/auth/login", new LoginDto(email, TestEnv.UserPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, res.StatusCode);
        var problem = await res.Content.ReadFromJsonAsync<ProblemDetails>();
        Assert.Contains("locked", problem!.Title);
    }
}
