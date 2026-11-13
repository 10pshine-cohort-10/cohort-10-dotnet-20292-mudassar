using System.ComponentModel.DataAnnotations;

namespace TaskManager.Api.DTOs;

public record RegisterDto(
    [Required, StringLength(100, MinimumLength = 2)] string FullName,
    [Required, EmailAddress, StringLength(256)] string Email,
    [Required, StringLength(100, MinimumLength = 8)] string Password);

public record LoginDto(
    [Required, EmailAddress] string Email,
    [Required] string Password);

public record UserInfoDto(Guid Id, string FullName, string Email, IList<string> Roles);

public record AuthResponse(string Token, DateTime ExpiresAt, UserInfoDto User);

