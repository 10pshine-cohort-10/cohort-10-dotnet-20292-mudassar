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
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth;
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
        Assert.NotNull(dto);
        Assert.Equal(userAuth.User.Email, dto.Email);

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
        Assert.NotNull(updatedUser);
        Assert.Equal([Roles.Admin], updatedUser.Roles);

        // User's pre-promotion token is now revoked because security stamp changed -> 401
        using var staleTokenReq = Authorized(HttpMethod.Get, "/api/auth/me", userAuth.Token);
        var staleTokenRes = await _client.SendAsync(staleTokenReq);
        Assert.Equal(HttpStatusCode.Unauthorized, staleTokenRes.StatusCode);

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

        // Existing JWT token of deactivated user is immediately rejected -> 401
        using var existingTokenReq = Authorized(HttpMethod.Get, "/api/auth/me", userAuth.Token);
        var existingTokenRes = await _client.SendAsync(existingTokenReq);
        Assert.Equal(HttpStatusCode.Unauthorized, existingTokenRes.StatusCode);

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
        Assert.NotNull(updated);
        Assert.Equal("After Name", updated.FullName);
    }

    [Fact]
    public async Task ChangeRole_ForNonExistentUser_Returns404()
    {
        var adminAuth = await LoginAdminAsync();
        using var req = Authorized(
            HttpMethod.Put,
            $"/api/users/{Guid.NewGuid()}/role",
            adminAuth.Token,
            new ChangeRoleDto(Roles.Admin));

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.NotFound, res.StatusCode);
    }

    [Fact]
    public async Task SetActive_TwoAdminsAuthenticatedBeforeFirstDeactivation_CannotLeaveZeroActiveAdmins()
    {
        var primaryAdmin = await LoginAdminAsync();
        var secondUser = await RegisterAndLoginUserAsync("Second Admin");

        // Promote second user to Admin and log them in before any deactivation occurs
        using (var promoteReq = Authorized(
            HttpMethod.Put,
            $"/api/users/{secondUser.User.Id}/role",
            primaryAdmin.Token,
            new ChangeRoleDto(Roles.Admin)))
        {
            (await _client.SendAsync(promoteReq)).EnsureSuccessStatusCode();
        }

        var secondAdminLogin = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(secondUser.User.Email, TestEnv.UserPassword));
        secondAdminLogin.EnsureSuccessStatusCode();
        var secondAdmin = await secondAdminLogin.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(secondAdmin);

        // Primary admin deactivates second admin -> succeeds (primary admin remains active)
        using (var deactivateSecondReq = Authorized(
            HttpMethod.Put,
            $"/api/users/{secondAdmin.User.Id}/active",
            primaryAdmin.Token,
            new SetActiveDto(false)))
        {
            var deactivateSecondRes = await _client.SendAsync(deactivateSecondReq);
            Assert.Equal(HttpStatusCode.NoContent, deactivateSecondRes.StatusCode);
        }

        // Second admin attempts to deactivate primary admin using their pre-issued token -> rejected
        using var deactivatePrimaryReq = Authorized(
            HttpMethod.Put,
            $"/api/users/{primaryAdmin.User.Id}/active",
            secondAdmin.Token,
            new SetActiveDto(false));
        var deactivatePrimaryRes = await _client.SendAsync(deactivatePrimaryReq);
        Assert.True(
            deactivatePrimaryRes.StatusCode is HttpStatusCode.Unauthorized or HttpStatusCode.BadRequest,
            $"Expected 401 or 400, got {deactivatePrimaryRes.StatusCode}");
    }
}

