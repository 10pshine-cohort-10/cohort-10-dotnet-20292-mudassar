using System.Text;
using Microsoft.Data.Sqlite;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.Logging.Abstractions;
using TaskManager.Api.Data;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;
using TaskManager.Api.Services;

namespace TaskManager.Tests.Services;

public sealed class TaskTransferServiceTests : IAsyncLifetime, IDisposable
{
    private readonly SqliteConnection _connection = new("DataSource=:memory:");
    private AppDbContext _db = default!;

    private ApplicationUser _owner = default!;
    private ApplicationUser _assignee = default!;
    private ApplicationUser _stranger = default!;
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
        _category = new Category { Name = "Work" };

        _db.Users.AddRange(_owner, _assignee, _stranger);
        _db.Categories.Add(_category);
        await _db.SaveChangesAsync();
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

    private static ApplicationUser NewUser(string fullName, string email) => new()
    {
        Id = Guid.NewGuid(),
        UserName = email,
        Email = email,
        FullName = fullName,
        IsActive = true
    };

    private TaskTransferService CreateSut() => new(_db, NullLogger<TaskTransferService>.Instance);

    [Fact]
    public async Task ExportJson_AndReimport_RoundTripsTasksAndEnforcesOwnership()
    {
        var taskSvc = new TaskService(_db, NullLogger<TaskService>.Instance);
        await taskSvc.CreateAsync(new Api.DTOs.CreateTaskDto(
            Title: "Owner Export Task",
            Description: "Exported description",
            Priority: TaskPriority.High,
            CategoryId: _category.Id,
            AssignedToId: _assignee.Id), _owner.Id);
        await taskSvc.CreateAsync(new Api.DTOs.CreateTaskDto("Stranger Secret Task"), _stranger.Id);

        var sut = CreateSut();
        var exported = await sut.ExportAsync(_owner.Id, isAdmin: false, format: "json");

        Assert.Equal("application/json; charset=utf-8", exported.ContentType);
        Assert.EndsWith(".json", exported.FileName, StringComparison.Ordinal);

        var jsonText = Encoding.UTF8.GetString(exported.Content);
        Assert.Contains("Owner Export Task", jsonText, StringComparison.Ordinal);
        Assert.DoesNotContain("Stranger Secret Task", jsonText, StringComparison.Ordinal);

        await using var importStream = new MemoryStream(exported.Content);
        var importResult = await sut.ImportAsync(importStream, "tasks.json", _owner.Id);

        Assert.Equal(1, importResult.ImportedCount);
        Assert.Equal(0, importResult.FailedCount);
        Assert.Empty(importResult.Errors);

        var ownerTasks = await _db.Tasks.Where(t => t.CreatedById == _owner.Id).ToListAsync();
        Assert.Equal(2, ownerTasks.Count);
        Assert.Contains(await _db.TaskHistories.ToListAsync(), h => h.Action == "Imported");
    }

    [Fact]
    public async Task ExportCsv_NeutralizesFormulaInjection_AndEscapesQuotesAndCommas()
    {
        _db.Tasks.Add(new TaskItem
        {
            Title = "=SUM(A1:A2), \"quoted\"",
            Description = "Line 1\nLine 2",
            Status = TaskItemStatus.InProgress,
            Priority = TaskPriority.Critical,
            CategoryId = _category.Id,
            CreatedById = _owner.Id,
            AssignedToId = _assignee.Id,
            CreatedAt = DateTime.UtcNow,
            RowVersion = Guid.NewGuid().ToByteArray()[..8]
        });
        await _db.SaveChangesAsync();

        var sut = CreateSut();
        var exported = await sut.ExportAsync(_owner.Id, isAdmin: false, format: "csv");

        Assert.Equal("text/csv; charset=utf-8", exported.ContentType);
        var csv = Encoding.UTF8.GetString(exported.Content);
        Assert.Contains("\"'=SUM(A1:A2), \"\"quoted\"\"\"", csv, StringComparison.Ordinal);
        Assert.Contains("\"Line 1\nLine 2\"", csv, StringComparison.Ordinal);
    }

    [Fact]
    public async Task ImportCsv_ValidAndInvalidRows_ImportsValidAndReportsRowErrors()
    {
        var csv = string.Join('\n', [
            "Title,Description,Status,Priority,DueDate,CategoryName,AssignedToEmail",
            "\"Imported CSV Task\",\"Details, with comma\",InProgress,High,2027-01-15,Work,assignee@test.local",
            "\"X\",\"Too short title\",Pending,Low,,,",
            "\"Bad Category Task\",\"Desc\",Pending,Medium,,MissingCategory,"
        ]);

        var sut = CreateSut();
        await using var stream = new MemoryStream(Encoding.UTF8.GetBytes(csv));
        var result = await sut.ImportAsync(stream, "tasks.csv", _owner.Id);

        Assert.Equal(1, result.ImportedCount);
        Assert.Equal(2, result.FailedCount);
        Assert.Equal(2, result.Errors.Count);
        Assert.Equal(2, result.Errors[0].RowNumber);
        Assert.Equal(3, result.Errors[1].RowNumber);

        var imported = await _db.Tasks.SingleAsync(t => t.Title == "Imported CSV Task");
        Assert.Equal("Details, with comma", imported.Description);
        Assert.Equal(TaskItemStatus.InProgress, imported.Status);
        Assert.Equal(TaskPriority.High, imported.Priority);
        Assert.Equal(_category.Id, imported.CategoryId);
        Assert.Equal(_assignee.Id, imported.AssignedToId);
    }

    [Fact]
    public async Task ExportAndImport_InvalidInputs_ThrowAppValidationException()
    {
        var sut = CreateSut();

        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.ExportAsync(_owner.Id, isAdmin: false, format: "xml"));

        await using var badExt = new MemoryStream(Encoding.UTF8.GetBytes("data"));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.ImportAsync(badExt, "tasks.txt", _owner.Id));

        await using var badJson = new MemoryStream(Encoding.UTF8.GetBytes("{ not json"));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.ImportAsync(badJson, "tasks.json", _owner.Id));

        await using var unterminatedCsv = new MemoryStream(Encoding.UTF8.GetBytes("Title,Description\n\"Unterminated,desc"));
        await Assert.ThrowsAsync<AppValidationException>(() =>
            sut.ImportAsync(unterminatedCsv, "tasks.csv", _owner.Id));
    }

    [Fact]
    public void Constructor_NullDependencies_ThrowsArgumentNullException()
    {
        Assert.Throws<ArgumentNullException>(() => new TaskTransferService(null!, NullLogger<TaskTransferService>.Instance));
        Assert.Throws<ArgumentNullException>(() => new TaskTransferService(_db, null!));
    }
}
