using System.Security.Claims;
using TaskManager.Api.Entities;
using TaskManager.Api.Exceptions;

namespace TaskManager.Api.Extensions;

public static class ClaimsPrincipalExtensions
{
    /// <summary>The user id always comes from the token, never from the request body.</summary>
    public static Guid GetUserId(this ClaimsPrincipal user)
    {
        var value = user.FindFirstValue(ClaimTypes.NameIdentifier);
        return Guid.TryParse(value, out var id) ? id : throw new UnauthorizedException("Invalid token.");
    }

    public static bool IsAdmin(this ClaimsPrincipal user) => user.IsInRole(Roles.Admin);
}

