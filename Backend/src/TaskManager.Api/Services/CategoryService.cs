using System.Diagnostics.CodeAnalysis;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;

namespace TaskManager.Api.Services;

public interface ICategoryService
{
    Task<IReadOnlyList<CategoryDto>> GetAllAsync();
    Task<CategoryDto> GetByIdAsync(int id);
    Task<CategoryDto> CreateAsync(SaveCategoryDto dto, Guid actingAdminId);
    Task<CategoryDto> UpdateAsync(int id, SaveCategoryDto dto, Guid actingAdminId);
    Task DeleteAsync(int id, Guid actingAdminId);
}

public class CategoryService(
    AppDbContext db,
    ILogger<CategoryService> log) : ICategoryService
{
    private const string CategoryNotFoundMessage = "Category was not found.";

    public async Task<IReadOnlyList<CategoryDto>> GetAllAsync()
    {
        return await db.Categories
            .AsNoTracking()
            .OrderBy(c => c.Name)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.Tasks.Count))
            .ToListAsync();
    }

    public async Task<CategoryDto> GetByIdAsync(int id)
    {
        var dto = await db.Categories
            .AsNoTracking()
            .Where(c => c.Id == id)
            .Select(c => new CategoryDto(c.Id, c.Name, c.Description, c.Tasks.Count))
            .FirstOrDefaultAsync();

        return dto ?? throw new NotFoundException(CategoryNotFoundMessage);
    }

    [SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "EF Core LINQ translator requires parameterless ToLower() to emit SQL LOWER().")]
    [SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "EF Core LINQ translator does not translate ToLowerInvariant().")]
    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "EF Core LINQ translator does not translate StringComparison overloads to SQL.")]
    public async Task<CategoryDto> CreateAsync(SaveCategoryDto dto, Guid actingAdminId)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var name = dto.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new AppValidationException("Category name is required.");

        var normalized = name.ToLowerInvariant();
        if (await db.Categories.AnyAsync(c => c.Name.ToLower() == normalized))
            throw new ConflictException($"Category '{name}' already exists.");

        var category = new Category
        {
            Name = name,
            Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim()
        };

        db.Categories.Add(category);
        await SaveWithUniqueGuardAsync(name);

        log.LogInformation(
            "Admin {AdminId} created category {CategoryId} ({CategoryName})",
            actingAdminId, category.Id, category.Name);

        return new CategoryDto(category.Id, category.Name, category.Description, 0);
    }

    [SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "EF Core LINQ translator requires parameterless ToLower() to emit SQL LOWER().")]
    [SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "EF Core LINQ translator does not translate ToLowerInvariant().")]
    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "EF Core LINQ translator does not translate StringComparison overloads to SQL.")]
    public async Task<CategoryDto> UpdateAsync(int id, SaveCategoryDto dto, Guid actingAdminId)
    {
        ArgumentNullException.ThrowIfNull(dto);

        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(CategoryNotFoundMessage);

        var name = dto.Name?.Trim() ?? string.Empty;
        if (string.IsNullOrWhiteSpace(name))
            throw new AppValidationException("Category name is required.");

        var normalized = name.ToLowerInvariant();
        if (await db.Categories.AnyAsync(c => c.Id != id && c.Name.ToLower() == normalized))
            throw new ConflictException($"Category '{name}' already exists.");

        category.Name = name;
        category.Description = string.IsNullOrWhiteSpace(dto.Description) ? null : dto.Description.Trim();

        await SaveWithUniqueGuardAsync(name);

        var taskCount = await db.Tasks.CountAsync(t => t.CategoryId == id);

        log.LogInformation(
            "Admin {AdminId} updated category {CategoryId} ({CategoryName})",
            actingAdminId, category.Id, category.Name);

        return new CategoryDto(category.Id, category.Name, category.Description, taskCount);
    }

    public async Task DeleteAsync(int id, Guid actingAdminId)
    {
        var category = await db.Categories.FirstOrDefaultAsync(c => c.Id == id)
            ?? throw new NotFoundException(CategoryNotFoundMessage);

        if (!db.Database.IsRelational())
        {
            var linkedTasks = await db.Tasks.IgnoreQueryFilters()
                .Where(t => t.CategoryId == id)
                .ToListAsync();
            foreach (var task in linkedTasks)
                task.CategoryId = null;
        }

        db.Categories.Remove(category);
        await db.SaveChangesAsync();

        log.LogInformation(
            "Admin {AdminId} deleted category {CategoryId} ({CategoryName})",
            actingAdminId, category.Id, category.Name);
    }

    private async Task SaveWithUniqueGuardAsync(string name)
    {
        try
        {
            await db.SaveChangesAsync();
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            log.LogWarning(ex, "Database unique constraint violation for category '{CategoryName}'", name);
            throw new ConflictException($"Category '{name}' already exists.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx && sqlEx.Number is 2601 or 2627)
            return true;

        var inner = ex.InnerException;
        if (inner is not null)
        {
            var typeName = inner.GetType().Name;
            if (string.Equals(typeName, "SqliteException", StringComparison.Ordinal) &&
                inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase))
                return true;
        }

        return false;
    }
}

