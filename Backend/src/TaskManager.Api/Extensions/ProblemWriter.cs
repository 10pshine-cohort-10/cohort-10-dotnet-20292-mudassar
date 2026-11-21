using Microsoft.AspNetCore.Mvc;

namespace TaskManager.Api.Extensions;

/// <summary>Writes a ProblemDetails body outside of MVC (e.g. JWT 401/403 events).</summary>
public static class ProblemWriter
{
    public static Task WriteAsync(HttpContext http, int status, string title)
    {
        http.Response.StatusCode = status;
        var problem = new ProblemDetails
        {
            Status = status,
            Title = title,
            Instance = http.Request.Path
        };
        problem.Extensions["traceId"] = http.TraceIdentifier;

        return http.Response.WriteAsJsonAsync(problem, (System.Text.Json.JsonSerializerOptions?)null, "application/problem+json");
    }
}

