using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.Entities;

namespace TaskManager.Tests.Data;

public class AppDbContextTests : IAsyncLifetime
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

    private async Task<ApplicationUser> SeedUserAsync()
    {
        var user = new ApplicationUser
        {
            Id = Guid.NewGuid(),
            UserName = $"u-{Guid.NewGuid():N}@test.local",
            Email = $"u-{Guid.NewGuid():N}@test.local",
            FullName = "Schema Tester"
        };
        _db.Users.Add(user);
        await _db.SaveChangesAsync();
        return user;
    }

    [Fact]
    public async Task SoftDeletedTask_IsHiddenByQueryFilter_ButRemainsInDatabase()
    {
        var user = await SeedUserAsync();
        var task = new TaskItem
        {
            Title = "Soft delete check",
            CreatedById = user.Id,
            IsDeleted = true
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        var visible = await _db.Tasks.ToListAsync();
        var allInDatabase = await _db.Tasks.IgnoreQueryFilters().ToListAsync();

        Assert.Empty(visible);
        var stored = Assert.Single(allInDatabase);
        Assert.True(stored.IsDeleted);
    }

    [Fact]
    public async Task Remove_TaskItem_DoesNotDeleteFromDatabase_SetsIsDeletedAndDeletedAt()
    {
        var user = await SeedUserAsync();
        var task = new TaskItem
        {
            Title = "Never physically delete me",
            CreatedById = user.Id
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        // Even if a caller uses Remove(), AppDbContext intercepts it and keeps the row in the DB
        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.Tasks.ToListAsync());

        var storedInDb = await _db.Tasks.IgnoreQueryFilters().SingleAsync(t => t.Id == task.Id);
        Assert.True(storedInDb.IsDeleted);
        Assert.NotNull(storedInDb.DeletedAt);
        Assert.NotNull(storedInDb.UpdatedAt);
    }

    [Fact]
    public async Task Category_UniqueName_ThrowsDbUpdateExceptionOnDuplicate()
    {
        _db.Categories.Add(new Category { Name = "Work" });
        await _db.SaveChangesAsync();

        _db.Categories.Add(new Category { Name = "Work" });
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());
    }

    [Fact]
    public async Task DeletingCategory_SetsTaskCategoryIdToNull_WithoutDeletingTask()
    {
        var user = await SeedUserAsync();
        var category = new Category { Name = "Temporary" };
        _db.Categories.Add(category);
        await _db.SaveChangesAsync();

        var task = new TaskItem
        {
            Title = "Categorized task",
            CreatedById = user.Id,
            CategoryId = category.Id
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        _db.Categories.Remove(category);
        await _db.SaveChangesAsync();

        var reloaded = await _db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Null(reloaded.CategoryId);
        Assert.False(reloaded.IsDeleted);
    }
}

