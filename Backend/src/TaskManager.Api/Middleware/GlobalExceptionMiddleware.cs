using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Exceptions;

namespace TaskManager.Api.Middleware;

/// <summary>Converts every exception into an RFC 7807 ProblemDetails response.</summary>
public class GlobalExceptionMiddleware(
    RequestDelegate next,
    ILogger<GlobalExceptionMiddleware> log,
    IHostEnvironment env)
{
    public async Task InvokeAsync(HttpContext ctx)
    {
        try
        {
            await next(ctx);
        }
        catch (Exception ex)
        {
            await HandleAsync(ctx, ex);
        }
    }

    private async Task HandleAsync(HttpContext ctx, Exception ex)
    {
        var (status, title) = ex switch
        {
            AppException app => (app.StatusCode, app.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The record was modified by another user."),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (status >= 500)
            log.LogError(ex, "Unhandled exception on {Method} {Path} (TraceId {TraceId})",
                ctx.Request.Method, ctx.Request.Path, ctx.TraceIdentifier);
        else
            log.LogWarning("Handled {Type} on {Path}: {Message}",
                ex.GetType().Name, ctx.Request.Path, ex.Message);

        if (ctx.Response.HasStarted)
            return;

        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = ctx.Request.Path,
            Detail = env.IsDevelopment() && status >= 500 ? ex.ToString() : null
        };
        problem.Extensions["traceId"] = ctx.TraceIdentifier;

        ctx.Response.Clear();
        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}

