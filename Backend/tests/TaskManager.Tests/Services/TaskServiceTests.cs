using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskManager.Api.Controllers;
using TaskManager.Api.Data;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;
using TaskManager.Api.Services;

namespace TaskManager.Tests.Services;

public class TaskServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private AppDbContext _db = default!;

    private ApplicationUser _owner = default!;
    private ApplicationUser _assignee = default!;
    private ApplicationUser _stranger = default!;
    private ApplicationUser _admin = default!;
    private Category _category = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();

        _owner = NewUser("Owner User", "owner@test.local");
        _assignee = NewUser("Assignee User", "assignee@test.local");
        _stranger = NewUser("Stranger User", "stranger@test.local");
        _admin = NewUser("Admin User", "admin@test.local");
        _category = new Category { Name = "Work" };

        _db.Users.AddRange(_owner, _assignee, _stranger, _admin);
        _db.Categories.Add(_category);
        await _db.SaveChangesAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private static ApplicationUser NewUser(string fullName, string email, bool active = true) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        FullName = fullName,
        IsActive = active
    };

    private TaskService CreateSut() => new(_db, NullLogger<TaskService>.Instance);

    [Fact]
    public async Task Create_PastDueDate_ThrowsAppValidationException()
    {
        var sut = CreateSut();
        var dto = new CreateTaskDto(
            Title: "Past Task",
            DueDate: DateTime.UtcNow.Date.AddDays(-1));

        await Assert.ThrowsAsync<AppValidationException>(() => sut.CreateAsync(dto, _owner.Id));
    }

    [Fact]
    public async Task Create_InvalidCategoryOrInactiveAssignee_ThrowsAppValidationException()
    {
        var sut = CreateSut();

        var badCategoryDto = new CreateTaskDto("Task", CategoryId: 99999);
        await Assert.ThrowsAsync<AppValidationException>(() => sut.CreateAsync(badCategoryDto, _owner.Id));

        var inactiveUser = NewUser("Inactive", "inactive@test.local", active: false);
        _db.Users.Add(inactiveUser);
        await _db.SaveChangesAsync();

        var inactiveAssigneeDto = new CreateTaskDto("Task", AssignedToId: inactiveUser.Id);
        await Assert.ThrowsAsync<AppValidationException>(() => sut.CreateAsync(inactiveAssigneeDto, _owner.Id));
    }

    [Fact]
    public async Task GetById_MissingThrowsNotFound_StrangerThrowsForbidden()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(new CreateTaskDto("Private Task"), _owner.Id);

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetByIdAsync(99999, _owner.Id, isAdmin: false));

        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.GetByIdAsync(created.Id, _stranger.Id, isAdmin: false));

        var ownerFetched = await sut.GetByIdAsync(created.Id, _owner.Id, isAdmin: false);
        Assert.Equal("Private Task", ownerFetched.Title);
    }

    [Fact]
    public async Task Update_AssigneeCanUpdate_StrangerCannot()
    {
        var sut = CreateSut();
        var created = await sut.CreateAsync(
            new CreateTaskDto("Shared Task", AssignedToId: _assignee.Id, CategoryId: _category.Id),
            _owner.Id);

        var updateDto = new UpdateTaskDto(
            Title: "Updated by Assignee",
            Description: "Progressing",
            Status: TaskItemStatus.InProgress,
            Priority: TaskPriority.High,
            CategoryId: _category.Id,
            AssignedToId: _assignee.Id,
            RowVersion: created.RowVersion);

        // Stranger is forbidden
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.UpdateAsync(created.Id, updateDto, _stranger.Id, isAdmin: false));

        // Assignee succeeds
        var updated = await sut.UpdateAsync(created.Id, updateDto, _assignee.Id, isAdmin: false);
        Assert.Equal("Updated by Assignee", updated.Title);
        Assert.Equal(TaskItemStatus.InProgress, updated.Status);
    }

    [Fact]
    public async Task Delete_ByNonOwnerThrowsForbidden_ByAdminOrOwnerSoftDeletes()
    {
        var sut = CreateSut();
        var task1 = await sut.CreateAsync(
            new CreateTaskDto("Task 1", AssignedToId: _assignee.Id),
            _owner.Id);

        // Assignee (non-owner) and stranger cannot delete
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.DeleteAsync(task1.Id, _assignee.Id, isAdmin: false));
        await Assert.ThrowsAsync<ForbiddenException>(() =>
            sut.DeleteAsync(task1.Id, _stranger.Id, isAdmin: false));

        // Admin can delete
        await sut.DeleteAsync(task1.Id, _admin.Id, isAdmin: true);

        // Soft-deleted: hidden from GetById, still in DB with IsDeleted = true
        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.GetByIdAsync(task1.Id, _admin.Id, isAdmin: true));

        var raw = await _db.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == task1.Id);
        Assert.True(raw.IsDeleted);
        Assert.NotNull(raw.DeletedAt);
    }

    [Fact]
    public async Task GetAll_UserListOnlyContainsOwnOrAssignedTasks_AdminSeesAll()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new CreateTaskDto("Owner Only"), _owner.Id);
        await sut.CreateAsync(new CreateTaskDto("Assigned to Owner", AssignedToId: _owner.Id), _stranger.Id);
        await sut.CreateAsync(new CreateTaskDto("Stranger Secret"), _stranger.Id);

        var ownerPage = await sut.GetAllAsync(_owner.Id, isAdmin: false);
        Assert.Equal(2, ownerPage.TotalCount);
        Assert.DoesNotContain(ownerPage.Items, t => t.Title == "Stranger Secret");

        var adminPage = await sut.GetAllAsync(_admin.Id, isAdmin: true);
        Assert.Equal(3, adminPage.TotalCount);
    }

    // ---------- Controller Unit Tests (Moq) ----------

    [Fact]
    public async Task Controller_Create_ReturnsCreatedAtActionResult_And_Delete_ReturnsNoContentResult()
    {
        var userId = Guid.NewGuid();
        var mockService = new Mock<ITaskService>();
        var fakeDto = new TaskDto(
            Id: 42,
            Title: "Mocked Task",
            Description: null,
            Status: TaskItemStatus.Pending,
            Priority: TaskPriority.Medium,
            DueDate: null,
            CategoryId: null,
            CategoryName: null,
            CreatedById: userId,
            CreatedByName: "Tester",
            AssignedToId: null,
            AssignedToName: null,
            CreatedAt: DateTime.UtcNow,
            UpdatedAt: null,
            RowVersion: "AQID");

        mockService.Setup(s => s.CreateAsync(It.IsAny<CreateTaskDto>(), userId))
                   .ReturnsAsync(fakeDto);
        mockService.Setup(s => s.DeleteAsync(42, userId, false))
                   .Returns(Task.CompletedTask);

        var controller = new TasksController(mockService.Object)
        {
            ControllerContext = new ControllerContext
            {
                HttpContext = new DefaultHttpContext
                {
                    User = new ClaimsPrincipal(new ClaimsIdentity(
                    [
                        new Claim(ClaimTypes.NameIdentifier, userId.ToString()),
                        new Claim(ClaimTypes.Role, Roles.User)
                    ], "TestAuth"))
                }
            }
        };

        var createAction = await controller.Create(new CreateTaskDto("Mocked Task"));
        var createdResult = Assert.IsType<CreatedAtActionResult>(createAction.Result);
        Assert.Equal(nameof(TasksController.GetById), createdResult.ActionName);
        Assert.Equal(42, createdResult.RouteValues!["id"]);

        var deleteAction = await controller.Delete(42);
        Assert.IsType<NoContentResult>(deleteAction);
    }
}

