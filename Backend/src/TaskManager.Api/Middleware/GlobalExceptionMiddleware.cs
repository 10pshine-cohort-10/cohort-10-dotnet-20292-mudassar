using System.Security.Claims;
using Microsoft.AspNetCore.Mvc;
using Microsoft.EntityFrameworkCore;
using Serilog.Context;
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
            var userId = GetUserId(ctx);
            using (LogContext.PushProperty("UserId", userId))
            using (LogContext.PushProperty("TraceId", ctx.TraceIdentifier))
            {
                if (ctx.Response.HasStarted)
                {
                    log.LogError(ex, "Unhandled exception after response started on {Method} {Path} for UserId {UserId} (TraceId {TraceId})",
                        ctx.Request.Method, ctx.Request.Path, userId, ctx.TraceIdentifier);
                    throw;
                }

                await HandleAsync(ctx, ex, userId);
            }
        }
    }

    private static string GetUserId(HttpContext ctx) =>
        ctx.User.Identity?.IsAuthenticated == true
            ? ctx.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown"
            : "Anonymous";

    private async Task HandleAsync(HttpContext ctx, Exception ex, string userId)
    {
        var (status, title) = ex switch
        {
            AppException app => (app.StatusCode, app.Message),
            BadHttpRequestException badReq => (badReq.StatusCode, badReq.Message),
            DbUpdateConcurrencyException => (StatusCodes.Status409Conflict, "The record was modified by another user."),
            UnauthorizedAccessException => (StatusCodes.Status401Unauthorized, "Unauthorized."),
            _ => (StatusCodes.Status500InternalServerError, "An unexpected error occurred.")
        };

        if (status >= 500)
            log.LogError(ex, "Unhandled exception on {Method} {Path} for UserId {UserId} (TraceId {TraceId})",
                ctx.Request.Method, ctx.Request.Path, userId, ctx.TraceIdentifier);
        else
            log.LogWarning(ex, "Handled {ExceptionType} ({StatusCode}) on {Method} {Path} for UserId {UserId} (TraceId {TraceId}): {Message}",
                ex.GetType().Name, status, ctx.Request.Method, ctx.Request.Path, userId, ctx.TraceIdentifier, ex.Message);

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
        if (ex is AppException appEx)
            problem.Extensions["errorCode"] = appEx.ErrorCode;

        // Preserve CORS headers set earlier in the pipeline before clearing the response.
        var corsHeaders = ctx.Response.Headers
            .Where(h => h.Key.StartsWith("Access-Control-", StringComparison.OrdinalIgnoreCase)
                     || string.Equals(h.Key, "Vary", StringComparison.OrdinalIgnoreCase))
            .ToList();

        ctx.Response.Clear();

        foreach (var (key, value) in corsHeaders)
            ctx.Response.Headers[key] = value;

        ctx.Response.StatusCode = status;
        await ctx.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}

