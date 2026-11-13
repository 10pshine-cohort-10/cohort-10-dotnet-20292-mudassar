using System.ComponentModel.DataAnnotations;

namespace TaskManager.Api.DTOs;

public record CategoryDto(
    int Id,
    string Name,
    string? Description,
    int TaskCount);

public record SaveCategoryDto(
    [Required, StringLength(100, MinimumLength = 1)] string Name,
    [StringLength(500)] string? Description = null);

