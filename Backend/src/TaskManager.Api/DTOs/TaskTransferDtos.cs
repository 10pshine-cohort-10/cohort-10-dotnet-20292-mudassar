using TaskManager.Api.Entities;

namespace TaskManager.Api.DTOs;

public record TaskExportItemDto(
    string Title,
    string? Description,
    TaskItemStatus Status,
    TaskPriority Priority,
    DateTime? DueDate,
    string? CategoryName,
    string? AssignedToEmail);

public record TaskImportRowErrorDto(
    int RowNumber,
    string Error);

public record TaskImportResultDto(
    int ImportedCount,
    int FailedCount,
    IReadOnlyList<TaskImportRowErrorDto> Errors);

public record TaskExportFileDto(
    byte[] Content,
    string ContentType,
    string FileName);

