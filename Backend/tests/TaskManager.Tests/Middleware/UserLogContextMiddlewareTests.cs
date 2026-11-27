using System.Security.Claims;
using Microsoft.AspNetCore.Http;
using Serilog;
using Serilog.Core;
using Serilog.Events;
using TaskManager.Api.Middleware;

namespace TaskManager.Tests.Middleware;

public class UserLogContextMiddlewareTests
{
    private sealed class InMemorySink : ILogEventSink
    {
        public List<LogEvent> Events { get; } = [];
        public void Emit(LogEvent logEvent) => Events.Add(logEvent);
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedUser_PushesUserIdAndTraceIdIntoLogContext()
    {
        var sink = new InMemorySink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var expectedUserId = Guid.NewGuid().ToString();
        var ctx = new DefaultHttpContext { TraceIdentifier = "trace-123" };
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.NameIdentifier, expectedUserId)],
            authenticationType: "TestAuth"));

        var middleware = new UserLogContextMiddleware(_ =>
        {
            logger.Information("Inside pipeline");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx);

        var evt = Assert.Single(sink.Events);
        Assert.Equal($"\"{expectedUserId}\"", evt.Properties["UserId"].ToString());
        Assert.Equal("\"trace-123\"", evt.Properties["TraceId"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_AnonymousRequest_PushesAnonymousUserId()
    {
        var sink = new InMemorySink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var ctx = new DefaultHttpContext { TraceIdentifier = "trace-anon" };
        var middleware = new UserLogContextMiddleware(_ =>
        {
            logger.Information("Anonymous call");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("\"Anonymous\"", evt.Properties["UserId"].ToString());
    }

    [Fact]
    public async Task InvokeAsync_AuthenticatedWithoutNameIdentifier_PushesUnknownUserId()
    {
        var sink = new InMemorySink();
        using var logger = new LoggerConfiguration()
            .Enrich.FromLogContext()
            .WriteTo.Sink(sink)
            .CreateLogger();

        var ctx = new DefaultHttpContext { TraceIdentifier = "trace-no-id" };
        ctx.User = new ClaimsPrincipal(new ClaimsIdentity(
            [new Claim(ClaimTypes.Email, "noid@example.com")],
            authenticationType: "TestAuth"));

        var middleware = new UserLogContextMiddleware(_ =>
        {
            logger.Information("Authenticated without NameIdentifier");
            return Task.CompletedTask;
        });

        await middleware.InvokeAsync(ctx);

        var evt = Assert.Single(sink.Events);
        Assert.Equal("\"Unknown\"", evt.Properties["UserId"].ToString());
    }
}

