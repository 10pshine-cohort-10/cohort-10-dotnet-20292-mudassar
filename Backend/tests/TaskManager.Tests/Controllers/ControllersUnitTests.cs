using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskManager.Api.Controllers;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Services;

namespace TaskManager.Tests.Controllers;

public class ControllersUnitTests
{
    private static ControllerContext BuildContext(Guid userId, string role = Roles.User) => new()
    {
        HttpContext = new DefaultHttpContext
        {
            User = new ClaimsPrincipal(new ClaimsIdentity(
            [
                new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                new Claim(ClaimTypes.Role, role)
            ], "UnitTestAuth"))
        }
    };

    [Fact]
    public async Task AuthController_Register_ReturnsCreatedAtAction()
    {
        var userId = Guid.NewGuid();
        var authService = new Mock<IAuthService>();
        var dto = new RegisterDto("Test User", "test@example.com", "Password@123");
        var expected = new UserInfoDto(userId, dto.FullName, dto.Email, [Roles.User]);

        authService.Setup(s => s.RegisterAsync(dto)).ReturnsAsync(expected);

        var controller = new AuthController(authService.Object, NullLogger<AuthController>.Instance);
        var result = await controller.Register(dto);

        var created = Assert.IsType<CreatedAtActionResult>(result.Result);
        Assert.Equal(nameof(AuthController.Me), created.ActionName);
        Assert.Equal(expected, created.Value);
    }

    [Fact]
    public async Task UsersController_ChangeRoleAndSetActive_ReturnNoContent()
    {
        var adminId = Guid.NewGuid();
        var targetId = Guid.NewGuid();
        var userService = new Mock<IUserService>();

        var controller = new UsersController(userService.Object)
        {
            ControllerContext = BuildContext(adminId, Roles.Admin)
        };

        var roleRes = await controller.ChangeRole(targetId, new ChangeRoleDto(Roles.Admin));
        Assert.IsType<NoContentResult>(roleRes);
        userService.Verify(s => s.ChangeRoleAsync(targetId, Roles.Admin, adminId), Times.Once);

        var activeRes = await controller.SetActive(targetId, new SetActiveDto(false));
        Assert.IsType<NoContentResult>(activeRes);
        userService.Verify(s => s.SetActiveAsync(targetId, false, adminId), Times.Once);
    }

    [Fact]
    public async Task CategoriesController_CreateAndDelete_ReturnExpectedResults()
    {
        var adminId = Guid.NewGuid();
        var categoryService = new Mock<ICategoryService>();
        var createdDto = new CategoryDto(7, "Ops", "Operations", 0);

        categoryService.Setup(s => s.CreateAsync(It.IsAny<SaveCategoryDto>(), adminId))
                       .ReturnsAsync(createdDto);

        var controller = new CategoriesController(categoryService.Object)
        {
            ControllerContext = BuildContext(adminId, Roles.Admin)
        };

        var createRes = await controller.Create(new SaveCategoryDto("Ops", "Operations"));
        var created = Assert.IsType<CreatedAtActionResult>(createRes.Result);
        Assert.Equal(7, created.RouteValues!["id"]);

        var deleteRes = await controller.Delete(7);
        Assert.IsType<NoContentResult>(deleteRes);
        categoryService.Verify(s => s.DeleteAsync(7, adminId), Times.Once);
    }
}

