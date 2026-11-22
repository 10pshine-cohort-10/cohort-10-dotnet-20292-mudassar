using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.Entities;

namespace TaskManager.Tests.Data;

public sealed class AppDbContextTests : IAsyncLifetime, IDisposable
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

    public void Dispose()
    {
        _db?.Dispose();
        _connection.Dispose();
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

        var storedInDb = await _db.Tasks.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.True(storedInDb.IsDeleted);
        Assert.NotNull(storedInDb.DeletedAt);
        Assert.NotNull(storedInDb.UpdatedAt);
    }

    [Fact]
    public async Task Remove_TaskItemWithLoadedCommentsAndHistory_PreservesDependents()
    {
        var user = await SeedUserAsync();
        var task = new TaskItem { Title = "Task with children", CreatedById = user.Id };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        _db.TaskComments.Add(new TaskComment { TaskItemId = task.Id, UserId = user.Id, Text = "Keep me" });
        _db.TaskHistories.Add(new TaskHistory { TaskItemId = task.Id, UserId = user.Id, Action = "Created" });
        await _db.SaveChangesAsync();

        // Load task WITH dependents tracked, then call Remove()
        var loaded = await _db.Tasks
            .Include(t => t.Comments)
            .Include(t => t.History)
            .SingleAsync(t => t.Id == task.Id);

        _db.Tasks.Remove(loaded);
        await _db.SaveChangesAsync();

        Assert.Single(await _db.TaskComments.IgnoreQueryFilters().Where(c => c.TaskItemId == task.Id).ToListAsync());
        Assert.Single(await _db.TaskHistories.IgnoreQueryFilters().Where(h => h.TaskItemId == task.Id).ToListAsync());
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
    public async Task DeletingCategory_ReferencedByTask_ThrowsDbUpdateException()
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

        _db.ChangeTracker.Clear();

        var categoryToDelete = await _db.Categories.SingleAsync(c => c.Id == category.Id);
        _db.Categories.Remove(categoryToDelete);
        await Assert.ThrowsAsync<DbUpdateException>(() => _db.SaveChangesAsync());

        _db.ChangeTracker.Clear();
        var reloadedTask = await _db.Tasks.AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal(category.Id, reloadedTask.CategoryId);
    }

    [Fact]
    public async Task Remove_TaskItemWithPendingTitleEdit_DiscardsEditAndRotatesRowVersion()
    {
        var user = await SeedUserAsync();
        byte[] originalRowVersion = [1, 2, 3, 4, 5, 6, 7, 8];
        var task = new TaskItem
        {
            Title = "Original Title",
            CreatedById = user.Id,
            RowVersion = originalRowVersion
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        task.Title = "Pending Unwanted Edit";
        _db.Tasks.Remove(task);
        await _db.SaveChangesAsync();

        var storedInDb = await _db.Tasks.IgnoreQueryFilters().AsNoTracking().SingleAsync(t => t.Id == task.Id);
        Assert.Equal("Original Title", storedInDb.Title);
        Assert.True(storedInDb.IsDeleted);
        Assert.NotNull(storedInDb.RowVersion);
        Assert.False(storedInDb.RowVersion.SequenceEqual(originalRowVersion));
    }

    [Fact]
    public async Task Remove_TaskItemWithNewlyAddedHistory_PreservesAddedHistory()
    {
        var user = await SeedUserAsync();
        var task = new TaskItem
        {
            Title = "Task with delete audit",
            CreatedById = user.Id,
            RowVersion = [1, 2, 3, 4]
        };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        _db.Tasks.Remove(task);
        _db.TaskHistories.Add(new TaskHistory
        {
            TaskItemId = task.Id,
            UserId = user.Id,
            Action = "Deleted",
            OldValue = task.Title
        });
        await _db.SaveChangesAsync();

        var histories = await _db.TaskHistories.IgnoreQueryFilters()
            .Where(h => h.TaskItemId == task.Id)
            .ToListAsync();
        var deletedEntry = Assert.Single(histories);
        Assert.Equal("Deleted", deletedEntry.Action);
    }

    [Fact]
    public async Task TaskHistory_IsHiddenByQueryFilter_WhenTaskIsSoftDeleted()
    {
        var user = await SeedUserAsync();
        var task = new TaskItem { Title = "Audited task", CreatedById = user.Id };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        _db.TaskHistories.Add(new TaskHistory
        {
            TaskItemId = task.Id,
            UserId = user.Id,
            Action = "Created",
            NewValue = "Pending"
        });
        await _db.SaveChangesAsync();

        Assert.Single(await _db.TaskHistories.ToListAsync());

        task.IsDeleted = true;
        await _db.SaveChangesAsync();

        Assert.Empty(await _db.TaskHistories.ToListAsync());
        Assert.Single(await _db.TaskHistories.IgnoreQueryFilters().ToListAsync());
    }

    [Fact]
    public async Task TaskHistory_CascadeDeletesInDatabase_WhenTaskRowIsPhysicallyDeleted()
    {
        var user = await SeedUserAsync();
        var task = new TaskItem { Title = "Physical cascade check", CreatedById = user.Id };
        _db.Tasks.Add(task);
        await _db.SaveChangesAsync();

        _db.TaskHistories.Add(new TaskHistory
        {
            TaskItemId = task.Id,
            UserId = user.Id,
            Action = "Created",
            NewValue = "Pending"
        });
        await _db.SaveChangesAsync();

        Assert.Single(await _db.TaskHistories.IgnoreQueryFilters().Where(h => h.TaskItemId == task.Id).ToListAsync());

        // Bypass SaveChanges soft-delete interception to verify relational ON DELETE CASCADE on TaskHistories
        var deletedRows = await _db.Tasks.IgnoreQueryFilters()
            .Where(t => t.Id == task.Id)
            .ExecuteDeleteAsync();

        Assert.Equal(1, deletedRows);
        Assert.Empty(await _db.TaskHistories.IgnoreQueryFilters().Where(h => h.TaskItemId == task.Id).ToListAsync());
    }
}

