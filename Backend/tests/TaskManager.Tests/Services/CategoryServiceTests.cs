using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskManager.Api.Data;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;
using TaskManager.Api.Services;

namespace TaskManager.Tests.Services;

public class CategoryServiceTests : IAsyncLifetime
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private AppDbContext _db = default!;

    public async Task InitializeAsync()
    {
        await _connection.OpenAsync();
        var options = new DbContextOptionsBuilder<AppDbContext>()
            .UseSqlite(_connection)
            .Options;

        _db = new AppDbContext(options);
        await _db.Database.EnsureCreatedAsync();
    }

    public async Task DisposeAsync()
    {
        await _db.DisposeAsync();
        await _connection.DisposeAsync();
    }

    private CategoryService CreateSut() => new(_db, NullLogger<CategoryService>.Instance);

    [Fact]
    public async Task Create_TrimsName_AndReturnsCategoryDto()
    {
        var sut = CreateSut();

        var created = await sut.CreateAsync(new SaveCategoryDto("  Finance  ", "  Budget items  "), Guid.NewGuid());

        Assert.True(created.Id > 0);
        Assert.Equal("Finance", created.Name);
        Assert.Equal("Budget items", created.Description);
        Assert.Equal(0, created.TaskCount);
    }

    [Fact]
    public async Task Create_DuplicateDifferentCase_ThrowsConflictException()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new SaveCategoryDto("Work", "Work items"), Guid.NewGuid());

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.CreateAsync(new SaveCategoryDto("  wOrK ", "Duplicate case"), Guid.NewGuid()));
    }

    [Fact]
    public async Task Update_MissingCategory_ThrowsNotFoundException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<NotFoundException>(() =>
            sut.UpdateAsync(9999, new SaveCategoryDto("Missing"), Guid.NewGuid()));
    }

    [Fact]
    public async Task Update_DuplicateNameDifferentCase_ThrowsConflictException()
    {
        var sut = CreateSut();
        await sut.CreateAsync(new SaveCategoryDto("Personal"), Guid.NewGuid());
        var second = await sut.CreateAsync(new SaveCategoryDto("Urgent"), Guid.NewGuid());

        await Assert.ThrowsAsync<ConflictException>(() =>
            sut.UpdateAsync(second.Id, new SaveCategoryDto("personal"), Guid.NewGuid()));
    }

    [Fact]
    public async Task Delete_SetsTasksCategoryIdToNull_InSqlite()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "owner@test.local",
            Email = "owner@test.local",
            FullName = "Owner"
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var cat = await sut.CreateAsync(new SaveCategoryDto("TempCategory"), Guid.NewGuid());

        var task = new TaskItem
        {
            Title = "Task in TempCategory",
            CreatedById = user.Id,
            CategoryId = cat.Id
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        await sut.DeleteAsync(cat.Id, Guid.NewGuid());

        var reloadedTask = await _db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Null(reloadedTask.CategoryId);
        Assert.False(reloadedTask.IsDeleted);
        await Assert.ThrowsAsync<NotFoundException>(() => sut.GetByIdAsync(cat.Id));
    }

    [Fact]
    public async Task GetAll_ReturnsOrderedByNameWithActiveTaskCount()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = "counter@test.local",
            Email = "counter@test.local",
            FullName = "Counter"
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var zCat = await sut.CreateAsync(new SaveCategoryDto("Zeta"), Guid.NewGuid());
        var aCat = await sut.CreateAsync(new SaveCategoryDto("Alpha"), Guid.NewGuid());

        _db.Tasks.AddRange(
            new TaskItem { Title = "Active 1", CreatedById = user.Id, CategoryId = aCat.Id },
            new TaskItem { Title = "Soft deleted", CreatedById = user.Id, CategoryId = aCat.Id, IsDeleted = true });
        await _db.SaveChangesAsync();

        var all = await sut.GetAllAsync();

        Assert.Equal(2, all.Count);
        Assert.Equal("Alpha", all[0].Name);
        Assert.Equal(1, all[0].TaskCount);
        Assert.Equal("Zeta", all[1].Name);
        Assert.Equal(0, all[1].TaskCount);
    }

    [Fact]
    public async Task GetById_MissingCategory_ThrowsNotFoundException()
    {
        await Assert.ThrowsAsync<NotFoundException>(() => CreateSut().GetByIdAsync(4040));
    }
}

