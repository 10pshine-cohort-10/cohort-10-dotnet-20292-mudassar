using System.Security.Claims;
using Serilog.Context;

namespace TaskManager.Api.Middleware;

/// <summary>
/// Enriches every log line produced during a request with the authenticated UserId and TraceId.
/// </summary>
public class UserLogContextMiddleware(RequestDelegate next)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        var userId = ctx.User.Identity?.IsAuthenticated == true
            ? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown"
            : "Anonymous";

        using (LogContext.PushProperty("UserId", userId))
        using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
        {
            await next(ctx);
        }
    }
}
