using Microsoft.AspNetCore.Identity;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;
using TaskManager.Api.Services;

namespace TaskManager.Tests.Services;

public class UserServiceTests
{
    private readonly Mock<UserManager<ApplicationUser>> _users = MockUserManager();

    private UserService CreateSut() => new(_users.Object, NullLogger<UserService>.Instance);

    private static Mock<UserManager<ApplicationUser>> MockUserManager() =>
        new(Mock.Of<IUserStore<ApplicationUser>>(), null!, null!, null!, null!, null!, null!, null!, null!);

    private static ApplicationUser NewUser(string name = "Jane Doe", string email = "jane@example.com", bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        Email = email,
        UserName = email,
        FullName = name,
        IsActive = active,
        CreatedAt = DateTime.UtcNow
    };

    // ---------- GetById ----------

    [Fact]
    public async Task GetById_UnknownUser_ThrowsNotFound()
    {
        _users.Setup(u => u.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        await Assert.ThrowsAsync<NotFoundException>(() => CreateSut().GetByIdAsync(Guid.NewGuid()));
    }

    [Fact]
    public async Task GetById_ExistingUser_ReturnsDtoWithRoles()
    {
        var user = NewUser();
        _users.Setup(u => u.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _users.Setup(u => u.GetRolesAsync(user)).ReturnsAsync([Roles.User]);

        var dto = await CreateSut().GetByIdAsync(user.Id);

        Assert.Equal(user.Id, dto.Id);
        Assert.Equal(user.FullName, dto.FullName);
        Assert.Equal(user.Email, dto.Email);
        Assert.Equal([Roles.User], dto.Roles);
        Assert.True(dto.IsActive);
    }

    // ---------- GetAll & GetLookup ----------

    [Fact]
    public async Task GetAll_FiltersBySearchAndActiveOnly()
    {
        var activeAlice = NewUser("Alice Smith", "alice@example.com", active: true);
        var inactiveBob = NewUser("Bob Jones", "bob@example.com", active: false);
        var activeCharlie = NewUser("Charlie Alice", "charlie@example.com", active: true);
        var all = new[] { activeAlice, inactiveBob, activeCharlie }.AsQueryable();

        _users.Setup(u => u.Users).Returns(all);
        _users.Setup(u => u.GetRolesAsync(It.IsAny<ApplicationUser>())).ReturnsAsync([Roles.User]);

        var sut = CreateSut();

        var onlyActive = await sut.GetAllAsync(activeOnly: true);
        Assert.Equal(2, onlyActive.Count);
        Assert.DoesNotContain(onlyActive, u => u.Id == inactiveBob.Id);

        var searched = await sut.GetAllAsync(search: "alice");
        Assert.Equal(2, searched.Count);
    }

    [Fact]
    public async Task GetLookup_ReturnsOnlyActiveUsersOrderedByName()
    {
        var activeZoe = NewUser("Zoe", "zoe@example.com", active: true);
        var activeAdam = NewUser("Adam", "adam@example.com", active: true);
        var inactiveBob = NewUser("Bob", "bob@example.com", active: false);

        _users.Setup(u => u.Users).Returns(new[] { activeZoe, activeAdam, inactiveBob }.AsQueryable());

        var lookup = await CreateSut().GetLookupAsync();

        Assert.Equal(2, lookup.Count);
        Assert.Equal("Adam", lookup[0].FullName);
        Assert.Equal("Zoe", lookup[1].FullName);
    }

    // ---------- ChangeRole ----------

    [Theory]
    [InlineData("")]
    [InlineData("SuperAdmin")]
    [InlineData("Manager")]
    public async Task ChangeRole_InvalidRole_ThrowsAppValidationException(string invalidRole)
    {
        await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateSut().ChangeRoleAsync(Guid.NewGuid(), invalidRole, Guid.NewGuid()));
    }

    [Fact]
    public async Task ChangeRole_SelfDemotion_ThrowsAppValidationException()
    {
        var adminId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateSut().ChangeRoleAsync(adminId, Roles.User, adminId));

        Assert.Contains("cannot demote yourself", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeRole_UserNotFound_ThrowsNotFoundException()
    {
        _users.Setup(u => u.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateSut().ChangeRoleAsync(Guid.NewGuid(), Roles.Admin, Guid.NewGuid()));
    }

    [Fact]
    public async Task ChangeRole_LastActiveAdmin_ThrowsAppValidationException()
    {
        var targetAdmin = NewUser(active: true);
        var actingAdminId = Guid.NewGuid();

        _users.Setup(u => u.FindByIdAsync(targetAdmin.Id.ToString())).ReturnsAsync(targetAdmin);
        _users.Setup(u => u.GetRolesAsync(targetAdmin)).ReturnsAsync([Roles.Admin]);
        _users.Setup(u => u.GetUsersInRoleAsync(Roles.Admin)).ReturnsAsync([targetAdmin]);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateSut().ChangeRoleAsync(targetAdmin.Id, Roles.User, actingAdminId));

        Assert.Contains("last active admin", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task ChangeRole_ValidRole_RemovesExistingRolesAndAddsNewRole()
    {
        var user = NewUser();
        var adminId = Guid.NewGuid();

        _users.Setup(u => u.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _users.Setup(u => u.GetRolesAsync(user)).ReturnsAsync([Roles.User]);
        _users.Setup(u => u.RemoveFromRolesAsync(user, It.IsAny<IEnumerable<string>>()))
              .ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.AddToRoleAsync(user, Roles.Admin))
              .ReturnsAsync(IdentityResult.Success);

        await CreateSut().ChangeRoleAsync(user.Id, "admin", adminId);

        _users.Verify(u => u.RemoveFromRolesAsync(user, It.Is<IEnumerable<string>>(r => r.Contains(Roles.User))), Times.Once);
        _users.Verify(u => u.AddToRoleAsync(user, Roles.Admin), Times.Once);
    }

    // ---------- SetActive ----------

    [Fact]
    public async Task SetActive_SelfDeactivation_ThrowsAppValidationException()
    {
        var adminId = Guid.NewGuid();

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateSut().SetActiveAsync(adminId, isActive: false, adminId));

        Assert.Contains("cannot deactivate yourself", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetActive_UserNotFound_ThrowsNotFoundException()
    {
        _users.Setup(u => u.FindByIdAsync(It.IsAny<string>())).ReturnsAsync((ApplicationUser?)null);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            CreateSut().SetActiveAsync(Guid.NewGuid(), isActive: false, Guid.NewGuid()));
    }

    [Fact]
    public async Task SetActive_DeactivateLastActiveAdmin_ThrowsAppValidationException()
    {
        var targetAdmin = NewUser(active: true);
        var actingAdminId = Guid.NewGuid();

        _users.Setup(u => u.FindByIdAsync(targetAdmin.Id.ToString())).ReturnsAsync(targetAdmin);
        _users.Setup(u => u.GetRolesAsync(targetAdmin)).ReturnsAsync([Roles.Admin]);
        _users.Setup(u => u.GetUsersInRoleAsync(Roles.Admin)).ReturnsAsync([targetAdmin]);

        var ex = await Assert.ThrowsAsync<AppValidationException>(() =>
            CreateSut().SetActiveAsync(targetAdmin.Id, isActive: false, actingAdminId));

        Assert.Contains("last active admin", ex.Message, StringComparison.OrdinalIgnoreCase);
    }

    [Fact]
    public async Task SetActive_DeactivateUser_UpdatesFlagAndSecurityStamp()
    {
        var user = NewUser(active: true);
        var adminId = Guid.NewGuid();

        _users.Setup(u => u.FindByIdAsync(user.Id.ToString())).ReturnsAsync(user);
        _users.Setup(u => u.GetRolesAsync(user)).ReturnsAsync([Roles.User]);
        _users.Setup(u => u.UpdateAsync(user)).ReturnsAsync(IdentityResult.Success);
        _users.Setup(u => u.UpdateSecurityStampAsync(user)).ReturnsAsync(IdentityResult.Success);

        await CreateSut().SetActiveAsync(user.Id, isActive: false, adminId);

        Assert.False(user.IsActive);
        _users.Verify(u => u.UpdateAsync(user), Times.Once);
        _users.Verify(u => u.UpdateSecurityStampAsync(user), Times.Once);
    }
}

