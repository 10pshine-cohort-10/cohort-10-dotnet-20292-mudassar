using System.Security.Claims;
using Serilog.Context;

namespace TaskManager.Api.Middleware;

/// <summary>
/// Enriches every Serilog log entry within the HTTP request scope with the authenticated
/// user's ID (from the JWT token claim) or "Anonymous" when unauthenticated.
/// </summary>
public class UserLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext context)
    {
        var userId = context.User.Identity?.IsAuthenticated == true
            ? context.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown"
            : "Anonymous";

        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("TraceId", context.TraceIdentifier))
        {
            await next(context);
        }
    }
}

