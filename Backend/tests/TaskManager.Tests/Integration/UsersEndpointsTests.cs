using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Integration;

public class UsersEndpointsTests(TestApiFactory factory) : IClassFixture<TestApiFactory>
{
    private readonly HttpClient _client = factory.CreateClient();

    private async Task<AuthResponse> LoginAdminAsync()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(factory.AdminEmail, factory.AdminPassword));
        res.EnsureSuccessStatusCode();
        return (await res.Content.ReadFromJsonAsync<AuthResponse>())!;
    }

    private async Task<AuthResponse> RegisterAndLoginUserAsync(string? fullName = null)
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto(fullName ?? TestEnv.UserName, email, TestEnv.UserPassword));
        reg.EnsureSuccessStatusCode();

        var login = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(email, TestEnv.UserPassword));
        login.EnsureSuccessStatusCode();
        return (await login.Content.ReadFromJsonAsync<AuthResponse>())!;
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
    public async Task GetUsers_AsRegularUser_Returns403Forbidden()
    {
        var userAuth = await RegisterAndLoginUserAsync();
        using var req = Authorized(HttpMethod.Get, "/api/users", userAuth.Token);

        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GetUsers_AsAdmin_Returns200WithUsers()
    {
        var adminAuth = await LoginAdminAsync();
        var userAuth = await RegisterAndLoginUserAsync("Searchable Member");

        using var req = Authorized(HttpMethod.Get, "/api/users?search=Searchable&activeOnly=true", adminAuth.Token);
        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var list = await res.Content.ReadFromJsonAsync<List<UserDto>>();
        Assert.NotNull(list);
        Assert.Contains(list, u => u.Id == userAuth.User.Id && u.FullName == "Searchable Member");
    }

    [Fact]
    public async Task GetLookup_AsRegularUser_Returns200WithActiveUsers()
    {
        var userAuth = await RegisterAndLoginUserAsync("Lookup User");
        using var req = Authorized(HttpMethod.Get, "/api/users/lookup", userAuth.Token);

        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var list = await res.Content.ReadFromJsonAsync<List<UserLookupDto>>();
        Assert.NotNull(list);
        Assert.Contains(list, u => u.Id == userAuth.User.Id && u.FullName == "Lookup User");
    }

    [Fact]
    public async Task GetById_AsAdmin_Returns200Or404()
    {
        var adminAuth = await LoginAdminAsync();
        var userAuth = await RegisterAndLoginUserAsync();

        using var okReq = Authorized(HttpMethod.Get, $"/api/users/{userAuth.User.Id}", adminAuth.Token);
        var okRes = await _client.SendAsync(okReq);
        Assert.Equal(HttpStatusCode.OK, okRes.StatusCode);
        var dto = await okRes.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal(userAuth.User.Email, dto!.Email);

        using var missingReq = Authorized(HttpMethod.Get, $"/api/users/{Guid.NewGuid()}", adminAuth.Token);
        var missingRes = await _client.SendAsync(missingReq);
        Assert.Equal(HttpStatusCode.NotFound, missingRes.StatusCode);
    }

    [Fact]
    public async Task ChangeRole_AsAdmin_PromotesUserAndRejectsInvalidOrSelfDemotion()
    {
        var adminAuth = await LoginAdminAsync();
        var userAuth = await RegisterAndLoginUserAsync();

        // Valid promotion to Admin -> 204
        using var promoteReq = Authorized(
            HttpMethod.Put,
            $"/api/users/{userAuth.User.Id}/role",
            adminAuth.Token,
            new ChangeRoleDto(Roles.Admin));
        var promoteRes = await _client.SendAsync(promoteReq);
        Assert.Equal(HttpStatusCode.NoContent, promoteRes.StatusCode);

        // Verify role updated
        using var getReq = Authorized(HttpMethod.Get, $"/api/users/{userAuth.User.Id}", adminAuth.Token);
        var getRes = await _client.SendAsync(getReq);
        var updatedUser = await getRes.Content.ReadFromJsonAsync<UserDto>();
        Assert.Equal([Roles.Admin], updatedUser!.Roles);

        // Invalid role -> 400
        using var badRoleReq = Authorized(
            HttpMethod.Put,
            $"/api/users/{userAuth.User.Id}/role",
            adminAuth.Token,
            new ChangeRoleDto("NotARole"));
        var badRoleRes = await _client.SendAsync(badRoleReq);
        Assert.Equal(HttpStatusCode.BadRequest, badRoleRes.StatusCode);

        // Self-demotion -> 400
        using var selfDemoteReq = Authorized(
            HttpMethod.Put,
            $"/api/users/{adminAuth.User.Id}/role",
            adminAuth.Token,
            new ChangeRoleDto(Roles.User));
        var selfDemoteRes = await _client.SendAsync(selfDemoteReq);
        Assert.Equal(HttpStatusCode.BadRequest, selfDemoteRes.StatusCode);
    }

    [Fact]
    public async Task SetActive_DeactivatesUser_WhoThenCannotLogin_AndBlocksSelfDeactivation()
    {
        var adminAuth = await LoginAdminAsync();
        var userAuth = await RegisterAndLoginUserAsync();

        // Self-deactivation -> 400
        using var selfDeactivateReq = Authorized(
            HttpMethod.Put,
            $"/api/users/{adminAuth.User.Id}/active",
            adminAuth.Token,
            new SetActiveDto(false));
        var selfDeactivateRes = await _client.SendAsync(selfDeactivateReq);
        Assert.Equal(HttpStatusCode.BadRequest, selfDeactivateRes.StatusCode);

        // Deactivate target user -> 204
        using var deactivateReq = Authorized(
            HttpMethod.Put,
            $"/api/users/{userAuth.User.Id}/active",
            adminAuth.Token,
            new SetActiveDto(false));
        var deactivateRes = await _client.SendAsync(deactivateReq);
        Assert.Equal(HttpStatusCode.NoContent, deactivateRes.StatusCode);

        // Deactivated user cannot log in -> 401
        var loginAttempt = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(userAuth.User.Email, TestEnv.UserPassword));
        Assert.Equal(HttpStatusCode.Unauthorized, loginAttempt.StatusCode);
    }

    [Fact]
    public async Task PutAuthMe_UpdatesCurrentUserFullName()
    {
        var userAuth = await RegisterAndLoginUserAsync("Before Name");

        using var updateReq = Authorized(
            HttpMethod.Put,
            "/api/auth/me",
            userAuth.Token,
            new UpdateProfileDto("After Name"));
        var res = await _client.SendAsync(updateReq);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var updated = await res.Content.ReadFromJsonAsync<UserInfoDto>();
        Assert.Equal("After Name", updated!.FullName);
    }
}

