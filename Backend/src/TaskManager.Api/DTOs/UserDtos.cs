using System.ComponentModel.DataAnnotations;

namespace TaskManager.Api.DTOs;

public record UserDto(
    Guid Id,
    string FullName,
    string Email,
    IList<string> Roles,
    bool IsActive,
    DateTime CreatedAt);

public record UserLookupDto(Guid Id, string FullName);

public record ChangeRoleDto([Required] string Role);

public record SetActiveDto(bool IsActive);

