using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;
using TaskManager.Api.Services;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Services;

public class AuthServiceTests : IAsyncLifetime
{
    private readonly Mock<UserManager<ApplicationUser>> _users = MockUserManager();
    private readonly Mock<IJwtTokenService> _jwt = new();

    public Task InitializeAsync() => TestEnv.EnsureLoadedAsync();

    public Task DisposeAsync() => Task.CompletedTask;

    private AuthService CreateSut() => new(_users.Object, _jwt.Object, NullLogger<AuthService>.Instance);

    private static Mock<UserManager<ApplicationUser>> MockUserManager() =>
        new(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    private static ApplicationUser NewUser(bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Email = TestEnv.UserEmail,
        UserName = TestEnv.UserEmail,
        FullName = TestEnv.UserName,
        IsActive = active
    };

    private static RegisterDto ValidRegister => new(TestEnv.UserName, TestEnv.UserEmail, TestEnv.UserPassword);
    private static LoginDto ValidLogin => new(TestEnv.UserEmail, TestEnv.UserPassword);

    // ---------- Register ----------

    [Fact]
    public async Task Register_DuplicateEmail_ReturnsGenericResponseWithoutCreatingUser()
    {
        _users.Setup(u => u.FindByEmailAsync(ValidRegister.Email)).ReturnsAsync(NewUser());

        var result = await CreateSut().RegisterAsync(ValidRegister);

        Assert.Equal(Guid.Empty, result.Id);
        Assert.Equal(new[] { Roles.User }, result.Roles);
        _users.Verify(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Register_NullOrBlankInputs_ThrowsExpectedExceptions()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<ArgumentNullException>(() => sut.RegisterAsync(null!));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.RegisterAsync(new RegisterDto("Valid User", "   ", TestEnv.UserPassword)));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.RegisterAsync(new RegisterDto("   ", TestEnv.UserEmail, TestEnv.UserPassword)));
    }

    [Fact]
    public async Task Register_WeakPassword_ThrowsValidation()
    {
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        _users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Passwords must have at least one digit." }));

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => CreateSut().RegisterAsync(ValidRegister));
        Assert.Contains("digit", ex.Message);
    }

    [Fact]
    public async Task Register_Success_AssignsUserRoleOnly()
    {
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        _users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);

        var result = await CreateSut().RegisterAsync(ValidRegister);

        _users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), Roles.User), Times.Once);
        _users.Verify(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), Roles.Admin), Times.Never);
        Assert.Equal(new[] { Roles.User }, result.Roles);
        Assert.Equal(TestEnv.UserName, result.FullName);
    }

    [Fact]
    public async Task Register_RoleAssignmentFails_DeletesCreatedUserAndThrows()
    {
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);
        _users.Setup(u => u.CreateAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>())).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.AddToRoleAsync(It.IsAny<ApplicationUser>(), Roles.User))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Role assignment failed." }));
        _users.Setup(u => u.DeleteAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(IdentityResult.Success);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() => CreateSut().RegisterAsync(ValidRegister));
        Assert.Contains("Role assignment failed", ex.Message);
        _users.Verify(u => u.DeleteAsync(It.IsAny<ApplicationUser>()), Times.Once);
    }

    // ---------- Login ----------

    [Fact]
    public async Task Login_UnknownEmail_ThrowsUnauthorized()
    {
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => CreateSut().LoginAsync(ValidLogin));
        Assert.Equal("Invalid email or password.", ex.Message);
    }

    [Fact]
    public async Task Login_InactiveUser_ThrowsUnauthorized()
    {
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(NewUser(active: false));

        await Assert.ThrowsAsync<UnauthorizedException>(() => CreateSut().LoginAsync(ValidLogin));
        _users.Verify(u => u.CheckPasswordAsync(It.IsAny<ApplicationUser>(), It.IsAny<string>()), Times.Never);
    }

    [Fact]
    public async Task Login_LockedOut_ThrowsGenericUnauthorized()
    {
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(NewUser());
        _users.Setup(u => u.IsLockedOutAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(true);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => CreateSut().LoginAsync(ValidLogin));
        Assert.Equal("Invalid email or password.", ex.Message);
    }

    [Fact]
    public async Task Login_WrongPassword_IncrementsAccessFailed()
    {
        var user = NewUser();
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        _users.Setup(u => u.IsLockedOutAsync(user)).ReturnsAsync(false);
        _users.Setup(u => u.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(false);
        _users.Setup(u => u.AccessFailedAsync(user)).ReturnsAsync(IdentityResult.Success);

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => CreateSut().LoginAsync(ValidLogin));

        Assert.Equal("Invalid email or password.", ex.Message);   // same message as unknown email
        _users.Verify(u => u.AccessFailedAsync(user), Times.Once);
    }

    [Fact]
    public async Task Login_Success_ReturnsTokenWithRoles()
    {
        var user = NewUser();
        var roles = new List<string> { Roles.User };
        var expires = DateTime.UtcNow.AddMinutes(60);

        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        _users.Setup(u => u.IsLockedOutAsync(user)).ReturnsAsync(false);
        _users.Setup(u => u.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(true);
        _users.Setup(u => u.ResetAccessFailedCountAsync(user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _users.Setup(u => u.GetRolesAsync(user)).ReturnsAsync(roles);
        _jwt.Setup(j => j.Create(user, roles)).Returns(("token-value", expires));

        var result = await CreateSut().LoginAsync(ValidLogin);

        Assert.Equal("token-value", result.Token);
        Assert.Equal(expires, result.ExpiresAt);
        Assert.Equal(user.Id, result.User.Id);
        Assert.Contains(Roles.User, result.User.Roles);
        _users.Verify(u => u.ResetAccessFailedCountAsync(user), Times.Once);
    }

    // ---------- Me ----------

    [Fact]
    public async Task GetCurrent_UnknownUser_ThrowsNotFound()
    {
        _users.Setup(u => u.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateSut().GetCurrentAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task UpdateProfile_ValidFullName_TrimsAndUpdatesUser()
    {
        var user = NewUser();
        _users.Setup(u => u.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _users.Setup(u => u.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.GetRolesAsync(user)).ReturnsAsync([Roles.User]);

        var updated = await CreateSut().UpdateProfileAsync(user.Id, new UpdateProfileDto("  Updated Name  "));

        Assert.Equal("Updated Name", updated.FullName);
        _users.Verify(u => u.UpdateAsync(user), Times.Once);
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() =>
            new AuthService(null!, _jwt.Object, NullLogger<AuthService>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new AuthService(_users.Object, null!, NullLogger<AuthService>.Instance));
        Assert.Throws<ArgumentNullException>(() =>
            new AuthService(_users.Object, _jwt.Object, null!));
    }

    [Fact]
    public async Task Login_ResetAccessFailedCountFails_ThrowsUnauthorizedWithoutIssuingToken()
    {
        var user = NewUser();
        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(user);
        _users.Setup(u => u.IsLockedOutAsync(user)).ReturnsAsync(false);
        _users.Setup(u => u.CheckPasswordAsync(user, It.IsAny<string>())).ReturnsAsync(true);
        _users.Setup(u => u.ResetAccessFailedCountAsync(user))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Description = "Concurrency failure" }));

        var ex = await Assert.ThrowsAsync<UnauthorizedException>(() => CreateSut().LoginAsync(ValidLogin));
        Assert.Equal("Invalid email or password.", ex.Message);
        _jwt.Verify(j => j.Create(It.IsAny<ApplicationUser>(), It.IsAny<IList<string>>()), Times.Never);
    }

    [Fact]
    public async Task Login_AccessFailedConcurrencyConflict_RetriesWithFreshUser()
    {
        var initialUser = NewUser();
        var reloadedUser = NewUser();
        reloadedUser.Id = initialUser.Id;

        _users.Setup(u => u.FindByEmailAsync(It.IsAny<string>())).ReturnsAsync(initialUser);
        _users.Setup(u => u.IsLockedOutAsync(It.IsAny<ApplicationUser>())).ReturnsAsync(false);
        _users.Setup(u => u.CheckPasswordAsync(initialUser, It.IsAny<string>())).ReturnsAsync(false);
        _users.Setup(u => u.AccessFailedAsync(initialUser))
              .ReturnsAsync(IdentityResult.Failed(new IdentityError { Code = "ConcurrencyFailure", Description = "Optimistic concurrency failure." }));
        _users.Setup(u => u.FindByIdAsync(initialUser.Id.ToString())).ReturnsAsync(reloadedUser);
        _users.Setup(u => u.AccessFailedAsync(reloadedUser)).ReturnsAsync(IdentityResult.Success);

        await Assert.ThrowsAsync<UnauthorizedException>(() => CreateSut().LoginAsync(ValidLogin));

        _users.Verify(u => u.AccessFailedAsync(initialUser), Times.Once);
        _users.Verify(u => u.AccessFailedAsync(reloadedUser), Times.Once);
    }
}

