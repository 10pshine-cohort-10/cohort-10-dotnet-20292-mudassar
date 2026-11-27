using System.Net;
using System.Net.Http.Headers;
using System.Net.Http.Json;
using TaskManager.Api.DTOs;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Integration;

public class CategoriesEndpointsTests : IClassFixture<TestApiFactory>
{
    private readonly TestApiFactory factory;
    private readonly HttpClient _client;

    public CategoriesEndpointsTests(TestApiFactory factory)
    {
        ArgumentNullException.ThrowIfNull(factory);
        this.factory = factory;
        _client = factory.CreateClient();
    }

    private async Task<AuthResponse> LoginAdminAsync()
    {
        var res = await _client.PostAsJsonAsync("/api/auth/login",
            new LoginDto(factory.AdminEmail, factory.AdminPassword));
        res.EnsureSuccessStatusCode();
        var auth = await res.Content.ReadFromJsonAsync<AuthResponse>();
        Assert.NotNull(auth);
        return auth;
    }

    private async Task<AuthResponse> RegisterAndLoginUserAsync()
    {
        var email = $"user-{Guid.NewGuid():N}@example.com";
        var reg = await _client.PostAsJsonAsync("/api/auth/register",
            new RegisterDto(TestEnv.UserName, email, TestEnv.UserPassword));
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
    public async Task PostCategory_AsRegularUser_Returns403Forbidden()
    {
        var userAuth = await RegisterAndLoginUserAsync();
        using var req = Authorized(
            HttpMethod.Post,
            "/api/categories",
            userAuth.Token,
            new SaveCategoryDto("Forbidden Category", "Not allowed"));

        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }

    [Fact]
    public async Task GetCategories_AsRegularUser_Returns200OrderedByName()
    {
        var userAuth = await RegisterAndLoginUserAsync();
        using var req = Authorized(HttpMethod.Get, "/api/categories", userAuth.Token);

        var res = await _client.SendAsync(req);

        Assert.Equal(HttpStatusCode.OK, res.StatusCode);
        var categories = await res.Content.ReadFromJsonAsync<List<CategoryDto>>();
        Assert.NotNull(categories);
        Assert.True(categories.Count >= 3);
        Assert.Equal(categories.Select(c => c.Name).OrderBy(n => n), categories.Select(c => c.Name));
    }

    private async Task<CategoryDto> CreateCategoryAsAdminAsync(string token, string name, string? description = null)
    {
        using var createReq = Authorized(
            HttpMethod.Post,
            "/api/categories",
            token,
            new SaveCategoryDto(name, description));
        var createRes = await _client.SendAsync(createReq);
        Assert.Equal(HttpStatusCode.Created, createRes.StatusCode);
        Assert.NotNull(createRes.Headers.Location);
        var created = await createRes.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(created);
        return created;
    }

    [Fact]
    public async Task CreateCategory_AsAdmin_Returns201WithLocation()
    {
        var adminAuth = await LoginAdminAsync();
        var uniqueName = $"Cat-{Guid.NewGuid():N}"[..16];

        var created = await CreateCategoryAsAdminAsync(adminAuth.Token, uniqueName, "Initial description");
        Assert.Equal(uniqueName, created.Name);
        Assert.Equal("Initial description", created.Description);
    }

    [Fact]
    public async Task CreateCategory_DuplicateDifferentCase_Returns409Conflict()
    {
        var adminAuth = await LoginAdminAsync();
        var uniqueName = $"Dup-{Guid.NewGuid():N}"[..16];
        await CreateCategoryAsAdminAsync(adminAuth.Token, uniqueName, "Initial");

        using var dupReq = Authorized(
            HttpMethod.Post,
            "/api/categories",
            adminAuth.Token,
            new SaveCategoryDto(uniqueName.ToUpperInvariant(), "Duplicate"));
        var dupRes = await _client.SendAsync(dupReq);
        Assert.Equal(HttpStatusCode.Conflict, dupRes.StatusCode);
    }

    [Fact]
    public async Task GetCategoryById_AsAdmin_Returns200()
    {
        var adminAuth = await LoginAdminAsync();
        var uniqueName = $"Get-{Guid.NewGuid():N}"[..16];
        var created = await CreateCategoryAsAdminAsync(adminAuth.Token, uniqueName);

        using var getReq = Authorized(HttpMethod.Get, $"/api/categories/{created.Id}", adminAuth.Token);
        var getRes = await _client.SendAsync(getReq);
        Assert.Equal(HttpStatusCode.OK, getRes.StatusCode);
    }

    [Fact]
    public async Task UpdateCategory_AsAdmin_Returns200WithUpdatedFields()
    {
        var adminAuth = await LoginAdminAsync();
        var uniqueName = $"Upd-{Guid.NewGuid():N}"[..16];
        var created = await CreateCategoryAsAdminAsync(adminAuth.Token, uniqueName, "Initial");

        var updatedName = $"{uniqueName}-Updated";
        using var updateReq = Authorized(
            HttpMethod.Put,
            $"/api/categories/{created.Id}",
            adminAuth.Token,
            new SaveCategoryDto(updatedName, "Updated description"));
        var updateRes = await _client.SendAsync(updateReq);
        Assert.Equal(HttpStatusCode.OK, updateRes.StatusCode);
        var updated = await updateRes.Content.ReadFromJsonAsync<CategoryDto>();
        Assert.NotNull(updated);
        Assert.Equal(updatedName, updated.Name);
        Assert.Equal("Updated description", updated.Description);
    }

    [Fact]
    public async Task DeleteCategory_AsAdmin_Returns204AndSubsequentGetReturns404()
    {
        var adminAuth = await LoginAdminAsync();
        var uniqueName = $"Del-{Guid.NewGuid():N}"[..16];
        var created = await CreateCategoryAsAdminAsync(adminAuth.Token, uniqueName);

        using var deleteReq = Authorized(HttpMethod.Delete, $"/api/categories/{created.Id}", adminAuth.Token);
        var deleteRes = await _client.SendAsync(deleteReq);
        Assert.Equal(HttpStatusCode.NoContent, deleteRes.StatusCode);

        using var getAfterDeleteReq = Authorized(HttpMethod.Get, $"/api/categories/{created.Id}", adminAuth.Token);
        var getAfterDeleteRes = await _client.SendAsync(getAfterDeleteReq);
        Assert.Equal(HttpStatusCode.NotFound, getAfterDeleteRes.StatusCode);
    }

    [Fact]
    public async Task DeleteCategory_AsRegularUser_Returns403Forbidden()
    {
        var userAuth = await RegisterAndLoginUserAsync();
        using var req = Authorized(HttpMethod.Delete, "/api/categories/1", userAuth.Token);

        var res = await _client.SendAsync(req);
        Assert.Equal(HttpStatusCode.Forbidden, res.StatusCode);
    }
}

