using System.Buffers;
using System.Diagnostics.CodeAnalysis;
using System.Globalization;
using System.Text;
using System.Text.Json;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.DTOs;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;

namespace TaskManager.Api.Services;

public interface ITaskTransferService
{
    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Matches task list query filter parameters for filtered export.")]
    Task<TaskExportFileDto> ExportAsync(
        Guid userId,
        bool isAdmin,
        string format = "json",
        TaskItemStatus? status = null,
        TaskPriority? priority = null,
        int? categoryId = null,
        Guid? assignedToId = null,
        string? search = null,
        CancellationToken cancellationToken = default);

    Task<TaskImportResultDto> ImportAsync(
        Stream fileStream,
        string fileName,
        Guid userId,
        CancellationToken cancellationToken = default);
}

public sealed class TaskTransferService : ITaskTransferService
{
    private const int MaxImportRows = 500;
    private const int MaxFileSizeBytes = 2 * 1024 * 1024;
    private static readonly SearchValues<char> FormulaTriggerChars =
        SearchValues.Create("=+-@\t\r");

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        PropertyNameCaseInsensitive = true,
        PropertyNamingPolicy = JsonNamingPolicy.CamelCase,
        WriteIndented = true,
        Converters = { new JsonStringEnumConverter() }
    };

    private readonly AppDbContext db;
    private readonly ILogger<TaskTransferService> log;

    public TaskTransferService(AppDbContext db, ILogger<TaskTransferService> log)
    {
        ArgumentNullException.ThrowIfNull(db);
        ArgumentNullException.ThrowIfNull(log);
        this.db = db;
        this.log = log;
    }

    [SuppressMessage("Major Code Smell", "S107:Methods should not have too many parameters", Justification = "Matches task list query filter parameters for filtered export.")]
    [SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "EF Core LINQ translator requires parameterless ToLower() to emit SQL LOWER().")]
    [SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "EF Core LINQ translator does not translate ToLowerInvariant().")]
    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "EF Core LINQ translator does not translate StringComparison overloads to SQL.")]
    public async Task<TaskExportFileDto> ExportAsync(
        Guid userId,
        bool isAdmin,
        string format = "json",
        TaskItemStatus? status = null,
        TaskPriority? priority = null,
        int? categoryId = null,
        Guid? assignedToId = null,
        string? search = null,
        CancellationToken cancellationToken = default)
    {
        var normalizedFormat = (format ?? "json").Trim().ToLowerInvariant();
        if (normalizedFormat is not ("json" or "csv"))
            throw new AppValidationException("Unsupported export format. Allowed formats: json, csv.");

        var query = BuildFilteredQuery(userId, isAdmin, status, priority, categoryId, assignedToId, search);
        var items = await query
            .Include(t => t.Category)
            .Include(t => t.AssignedTo)
            .OrderByDescending(t => t.CreatedAt)
            .ThenByDescending(t => t.Id)
            .Select(t => new TaskExportItemDto(
                t.Title,
                t.Description,
                t.Status,
                t.Priority,
                t.DueDate,
                t.Category != null ? t.Category.Name : null,
                t.AssignedTo != null ? t.AssignedTo.Email : null))
            .ToListAsync(cancellationToken);

        var timestamp = DateTime.UtcNow.ToString("yyyyMMdd-HHmmss", CultureInfo.InvariantCulture);
        log.LogInformation("User {UserId} exported {Count} tasks as {Format}", userId, items.Count, normalizedFormat);

        if (normalizedFormat == "csv")
        {
            var csvBytes = Encoding.UTF8.GetBytes(SerializeCsv(items));
            return new TaskExportFileDto(csvBytes, "text/csv; charset=utf-8", $"tasks-export-{timestamp}.csv");
        }

        var jsonBytes = JsonSerializer.SerializeToUtf8Bytes(items, JsonOptions);
        return new TaskExportFileDto(jsonBytes, "application/json; charset=utf-8", $"tasks-export-{timestamp}.json");
    }

    public async Task<TaskImportResultDto> ImportAsync(
        Stream fileStream,
        string fileName,
        Guid userId,
        CancellationToken cancellationToken = default)
    {
        ArgumentNullException.ThrowIfNull(fileStream);

        var extension = Path.GetExtension(fileName ?? string.Empty).ToLowerInvariant();
        if (extension is not (".json" or ".csv"))
            throw new AppValidationException("Unsupported import file type. Please upload a .json or .csv file.");

        var rawText = await ReadStreamBoundedAsync(fileStream, cancellationToken);
        if (string.IsNullOrWhiteSpace(rawText))
            throw new AppValidationException("The uploaded file is empty.");

        var parsedRows = extension == ".json"
            ? ParseJsonRows(rawText)
            : ParseCsvRows(rawText);

        if (parsedRows.Count == 0)
            throw new AppValidationException("No tasks were found in the uploaded file.");

        if (parsedRows.Count > MaxImportRows)
            throw new AppValidationException($"A maximum of {MaxImportRows} tasks can be imported per file.");

        var categoriesByName = await db.Categories
            .AsNoTracking()
            .ToDictionaryAsync(c => c.Name, c => c.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var activeUsersByEmail = await db.Users
            .AsNoTracking()
            .Where(u => u.IsActive && u.Email != null)
            .ToDictionaryAsync(u => u.Email!, u => u.Id, StringComparer.OrdinalIgnoreCase, cancellationToken);

        var errors = new List<TaskImportRowErrorDto>();
        var importedCount = 0;
        var isSqlServer = db.Database.IsSqlServer();
        var now = DateTime.UtcNow;

        foreach (var row in parsedRows)
        {
            var validationError = TryBuildTaskEntity(
                row,
                userId,
                categoriesByName,
                activeUsersByEmail,
                isSqlServer,
                now,
                out var entity);

            if (validationError is not null || entity is null)
            {
                errors.Add(new TaskImportRowErrorDto(row.RowNumber, validationError ?? "Invalid task row."));
                continue;
            }

            db.Tasks.Add(entity);
            db.TaskHistories.Add(new TaskHistory
            {
                TaskItem = entity,
                UserId = userId,
                Action = "Imported",
                NewValue = entity.Status.ToString()
            });
            importedCount++;
        }

        if (importedCount > 0)
            await db.SaveChangesAsync(cancellationToken);

        log.LogInformation(
            "User {UserId} imported {ImportedCount} tasks ({FailedCount} failed) from {FileName}",
            userId, importedCount, errors.Count, fileName);

        return new TaskImportResultDto(importedCount, errors.Count, errors);
    }

    [SuppressMessage("Globalization", "CA1304:Specify CultureInfo", Justification = "EF Core LINQ translator requires parameterless ToLower() to emit SQL LOWER().")]
    [SuppressMessage("Globalization", "CA1311:Specify a culture or use an invariant version", Justification = "EF Core LINQ translator does not translate ToLowerInvariant().")]
    [SuppressMessage("Performance", "CA1862:Use the 'StringComparison' method overloads to perform case-insensitive string comparisons", Justification = "EF Core LINQ translator does not translate StringComparison overloads to SQL.")]
    private IQueryable<TaskItem> BuildFilteredQuery(
        Guid userId,
        bool isAdmin,
        TaskItemStatus? status,
        TaskPriority? priority,
        int? categoryId,
        Guid? assignedToId,
        string? search)
    {
        var query = db.Tasks.AsNoTracking();

        if (!isAdmin)
            query = query.Where(t => t.CreatedById == userId || t.AssignedToId == userId);
        if (status.HasValue)
            query = query.Where(t => t.Status == status.Value);
        if (priority.HasValue)
            query = query.Where(t => t.Priority == priority.Value);
        if (categoryId.HasValue)
            query = query.Where(t => t.CategoryId == categoryId.Value);
        if (assignedToId.HasValue)
            query = query.Where(t => t.AssignedToId == assignedToId.Value);

        if (!string.IsNullOrWhiteSpace(search))
        {
            var term = search.Trim().ToLowerInvariant();
            query = query.Where(t =>
                t.Title.ToLower().Contains(term) ||
                (t.Description != null && t.Description.ToLower().Contains(term)) ||
                (t.AssignedTo != null && t.AssignedTo.FullName.ToLower().Contains(term)));
        }

        return query;
    }

    private static async Task<string> ReadStreamBoundedAsync(Stream stream, CancellationToken cancellationToken)
    {
        using var buffer = new MemoryStream();
        var chunk = new byte[8192];
        var total = 0;

        while (true)
        {
            var read = await stream.ReadAsync(chunk, cancellationToken);
            if (read == 0)
                break;

            total += read;
            if (total > MaxFileSizeBytes)
                throw new AppValidationException("Import file exceeds the 2 MB size limit.");

            await buffer.WriteAsync(chunk.AsMemory(0, read), cancellationToken);
        }

        return Encoding.UTF8.GetString(buffer.ToArray()).TrimStart('\uFEFF');
    }

    private static string SerializeCsv(IReadOnlyList<TaskExportItemDto> items)
    {
        var sb = new StringBuilder();
        sb.AppendLine("Title,Description,Status,Priority,DueDate,CategoryName,AssignedToEmail");

        foreach (var item in items)
        {
            var due = item.DueDate.HasValue
                ? item.DueDate.Value.ToString("yyyy-MM-dd", CultureInfo.InvariantCulture)
                : string.Empty;

            sb.Append(EscapeCsvField(item.Title)).Append(',')
              .Append(EscapeCsvField(item.Description)).Append(',')
              .Append(EscapeCsvField(item.Status.ToString())).Append(',')
              .Append(EscapeCsvField(item.Priority.ToString())).Append(',')
              .Append(EscapeCsvField(due)).Append(',')
              .Append(EscapeCsvField(item.CategoryName)).Append(',')
              .AppendLine(EscapeCsvField(item.AssignedToEmail));
        }

        return sb.ToString();
    }

    private static string EscapeCsvField(string? value)
    {
        if (string.IsNullOrEmpty(value))
            return string.Empty;

        var safe = value.AsSpan().IndexOfAny(FormulaTriggerChars) == 0 ? $"'{value}" : value;
        var needsQuoting = safe.Contains(',', StringComparison.Ordinal)
            || safe.Contains('"', StringComparison.Ordinal)
            || safe.Contains('\n', StringComparison.Ordinal)
            || safe.Contains('\r', StringComparison.Ordinal);

        return needsQuoting ? $"\"{safe.Replace("\"", "\"\"", StringComparison.Ordinal)}\"" : safe;
    }

    private static List<RawImportRow> ParseJsonRows(string rawText)
    {
        try
        {
            using var doc = JsonDocument.Parse(rawText);
            var arrayElement = doc.RootElement.ValueKind switch
            {
                JsonValueKind.Array => doc.RootElement,
                JsonValueKind.Object when TryGetTasksProperty(doc.RootElement, out var tasksProp) => tasksProp,
                _ => throw new AppValidationException("JSON import file must contain an array of tasks or an object with a 'tasks' array.")
            };

            var rows = new List<RawImportRow>();
            var rowNum = 0;
            foreach (var element in arrayElement.EnumerateArray())
            {
                rowNum++;
                rows.Add(ReadJsonRow(element, rowNum));
            }
            return rows;
        }
        catch (JsonException ex)
        {
            throw new AppValidationException($"Invalid JSON format: {ex.Message}");
        }
    }

    private static bool TryGetTasksProperty(JsonElement obj, out JsonElement tasksArray)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (string.Equals(prop.Name, "tasks", StringComparison.OrdinalIgnoreCase) &&
                prop.Value.ValueKind == JsonValueKind.Array)
            {
                tasksArray = prop.Value;
                return true;
            }
        }

        tasksArray = default;
        return false;
    }

    private static RawImportRow ReadJsonRow(JsonElement element, int rowNumber)
    {
        if (element.ValueKind != JsonValueKind.Object)
            return new RawImportRow(rowNumber, null, null, null, null, null, null, null, "Row must be a JSON object.");

        return new RawImportRow(
            rowNumber,
            GetJsonString(element, "title"),
            GetJsonString(element, "description"),
            GetJsonString(element, "status"),
            GetJsonString(element, "priority"),
            GetJsonString(element, "dueDate"),
            GetJsonString(element, "categoryName"),
            GetJsonString(element, "assignedToEmail"),
            null);
    }

    private static string? GetJsonString(JsonElement obj, string name)
    {
        foreach (var prop in obj.EnumerateObject())
        {
            if (!string.Equals(prop.Name, name, StringComparison.OrdinalIgnoreCase))
                continue;

            return prop.Value.ValueKind switch
            {
                JsonValueKind.String => prop.Value.GetString(),
                JsonValueKind.Null or JsonValueKind.Undefined => null,
                _ => prop.Value.ToString()
            };
        }

        return null;
    }

    private static List<RawImportRow> ParseCsvRows(string rawText)
    {
        var records = ParseCsvRecords(rawText);
        if (records.Count == 0)
            return [];

        var headerMap = BuildHeaderMap(records[0]);
        if (!headerMap.ContainsKey("title"))
            throw new AppValidationException("CSV file must include a 'Title' header column.");

        var rows = new List<RawImportRow>();
        for (var i = 1; i < records.Count; i++)
        {
            var record = records[i];
            if (record.All(string.IsNullOrWhiteSpace))
                continue;

            rows.Add(new RawImportRow(
                RowNumber: i,
                Title: GetCsvCell(record, headerMap, "title"),
                Description: GetCsvCell(record, headerMap, "description"),
                Status: GetCsvCell(record, headerMap, "status"),
                Priority: GetCsvCell(record, headerMap, "priority"),
                DueDate: GetCsvCell(record, headerMap, "duedate"),
                CategoryName: GetCsvCell(record, headerMap, "categoryname"),
                AssignedToEmail: GetCsvCell(record, headerMap, "assignedtoemail"),
                ParseError: null));
        }

        return rows;
    }

    private static Dictionary<string, int> BuildHeaderMap(List<string> headerRow)
    {
        var map = new Dictionary<string, int>(StringComparer.OrdinalIgnoreCase);
        for (var i = 0; i < headerRow.Count; i++)
        {
            var normalized = headerRow[i].Trim().Replace(" ", string.Empty, StringComparison.Ordinal);
            if (normalized.Length > 0 && !map.ContainsKey(normalized))
                map[normalized] = i;
        }
        return map;
    }

    private static string? GetCsvCell(List<string> record, Dictionary<string, int> headerMap, string key)
    {
        if (!headerMap.TryGetValue(key, out var idx) || idx >= record.Count)
            return null;
        return record[idx];
    }

    private static List<List<string>> ParseCsvRecords(string text)
    {
        var records = new List<List<string>>();
        var currentRow = new List<string>();
        var currentField = new StringBuilder();
        var index = 0;

        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '"' && currentField.Length == 0)
            {
                index = ReadQuotedField(text, index + 1, currentField);
                continue;
            }

            if (ch == ',')
            {
                currentRow.Add(currentField.ToString());
                currentField.Clear();
                index++;
                continue;
            }

            if (ch is '\r' or '\n')
            {
                index += ch == '\r' && index + 1 < text.Length && text[index + 1] == '\n' ? 2 : 1;
                currentRow.Add(currentField.ToString());
                currentField.Clear();
                records.Add(currentRow);
                currentRow = [];
                continue;
            }

            currentField.Append(ch);
            index++;
        }

        if (currentField.Length > 0 || currentRow.Count > 0)
        {
            currentRow.Add(currentField.ToString());
            records.Add(currentRow);
        }

        return records;
    }

    private static int ReadQuotedField(string text, int index, StringBuilder field)
    {
        while (index < text.Length)
        {
            var ch = text[index];
            if (ch == '"')
            {
                if (index + 1 < text.Length && text[index + 1] == '"')
                {
                    field.Append('"');
                    index += 2;
                    continue;
                }

                return index + 1;
            }

            field.Append(ch);
            index++;
        }

        return index;
    }

    private static string? TryBuildTaskEntity(
        RawImportRow row,
        Guid userId,
        Dictionary<string, int> categoriesByName,
        Dictionary<string, Guid> activeUsersByEmail,
        bool isSqlServer,
        DateTime now,
        out TaskItem? entity)
    {
        entity = null;
        if (row.ParseError is not null)
            return row.ParseError;

        var title = row.Title?.Trim() ?? string.Empty;
        if (title.Length is < 2 or > 200)
            return "Title must be between 2 and 200 characters.";

        var description = string.IsNullOrWhiteSpace(row.Description) ? null : row.Description.Trim();
        if (description is { Length: > 2000 })
            return "Description cannot exceed 2000 characters.";

        if (!TryParseEnum(row.Status, TaskItemStatus.Pending, out var status))
            return $"Invalid status '{row.Status}'.";

        if (!TryParseEnum(row.Priority, TaskPriority.Medium, out var priority))
            return $"Invalid priority '{row.Priority}'.";

        if (!TryParseOptionalDate(row.DueDate, out var dueDate))
            return $"Invalid due date '{row.DueDate}'.";

        int? categoryId = null;
        if (!string.IsNullOrWhiteSpace(row.CategoryName))
        {
            if (!categoriesByName.TryGetValue(row.CategoryName.Trim(), out var resolvedCatId))
                return $"Category '{row.CategoryName.Trim()}' does not exist.";
            categoryId = resolvedCatId;
        }

        Guid? assignedToId = null;
        if (!string.IsNullOrWhiteSpace(row.AssignedToEmail))
        {
            if (!activeUsersByEmail.TryGetValue(row.AssignedToEmail.Trim(), out var resolvedUserId))
                return $"Active user '{row.AssignedToEmail.Trim()}' was not found.";
            assignedToId = resolvedUserId;
        }

        entity = new TaskItem
        {
            Title = title,
            Description = description,
            Status = status,
            Priority = priority,
            DueDate = dueDate,
            CategoryId = categoryId,
            CreatedById = userId,
            AssignedToId = assignedToId,
            CreatedAt = now,
            RowVersion = isSqlServer ? [] : Guid.NewGuid().ToByteArray()[..8]
        };

        return null;
    }

    private static bool TryParseEnum<TEnum>(string? raw, TEnum defaultValue, out TEnum parsed)
        where TEnum : struct, Enum
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            parsed = defaultValue;
            return true;
        }

        return Enum.TryParse(raw.Trim(), ignoreCase: true, out parsed) && Enum.IsDefined(parsed);
    }

    private static bool TryParseOptionalDate(string? raw, out DateTime? parsed)
    {
        if (string.IsNullOrWhiteSpace(raw))
        {
            parsed = null;
            return true;
        }

        if (DateTime.TryParse(raw.Trim(), CultureInfo.InvariantCulture, DateTimeStyles.AssumeUniversal | DateTimeStyles.AdjustToUniversal, out var dt))
        {
            parsed = dt;
            return true;
        }

        parsed = null;
        return false;
    }

    private sealed record RawImportRow(
        int RowNumber,
        string? Title,
        string? Description,
        string? Status,
        string? Priority,
        string? DueDate,
        string? CategoryName,
        string? AssignedToEmail,
        string? ParseError);
}

