using System.Text.Json;
using Microsoft.AspNetCore.Http;
using Microsoft.AspNetCore.Mvc;
using Microsoft.Extensions.Hosting;
using Microsoft.Extensions.Logging.Abstractions;
using Moq;
using TaskManager.Api.Exceptions;
using TaskManager.Api.Middleware;

namespace TaskManager.Tests.Middleware;

public class GlobalExceptionMiddlewareTests
{
    private static GlobalExceptionMiddleware CreateSut(RequestDelegate next, bool isDevelopment = false)
    {
        var env = new Mock<IHostEnvironment>();
        env.SetupGet(e => e.EnvironmentName).Returns(isDevelopment ? Environments.Development : Environments.Production);
        return new GlobalExceptionMiddleware(next, NullLogger<GlobalExceptionMiddleware>.Instance, env.Object);
    }

    [Fact]
    public async Task InvokeAsync_AppException_ReturnsMatchingStatusAndProblemDetails()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/tasks/99";
        ctx.TraceIdentifier = "trace-404";

        var sut = CreateSut(_ => throw new NotFoundException("Task 99 was not found."));
        await sut.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status404NotFound, ctx.Response.StatusCode);
        Assert.StartsWith("application/problem+json", ctx.Response.ContentType);

        ctx.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            ctx.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.NotNull(problem);
        Assert.Equal(404, problem!.Status);
        Assert.Equal("Task 99 was not found.", problem.Title);
        Assert.Equal("/api/tasks/99", problem.Instance);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledExceptionInProduction_HidesStackTrace()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/boom";

        var sut = CreateSut(_ => throw new InvalidOperationException("sensitive internal failure"), isDevelopment: false);
        await sut.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);

        ctx.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            ctx.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Equal("An unexpected error occurred.", problem!.Title);
        Assert.Null(problem.Detail);
    }

    [Fact]
    public async Task InvokeAsync_ConcurrencyException_Returns409ConflictProblemDetails()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/tasks/1";

        var sut = CreateSut(_ => throw new Microsoft.EntityFrameworkCore.DbUpdateConcurrencyException("concurrency"));
        await sut.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status409Conflict, ctx.Response.StatusCode);

        ctx.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            ctx.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Equal(409, problem!.Status);
        Assert.Equal("The record was modified by another user.", problem.Title);
    }

    [Fact]
    public async Task InvokeAsync_UnhandledExceptionInDevelopment_IncludesStackTraceInDetail()
    {
        var ctx = new DefaultHttpContext();
        ctx.Response.Body = new MemoryStream();
        ctx.Request.Path = "/api/dev-boom";

        var sut = CreateSut(_ => throw new InvalidOperationException("dev stack check"), isDevelopment: true);
        await sut.InvokeAsync(ctx);

        Assert.Equal(StatusCodes.Status500InternalServerError, ctx.Response.StatusCode);

        ctx.Response.Body.Position = 0;
        var problem = await JsonSerializer.DeserializeAsync<ProblemDetails>(
            ctx.Response.Body,
            new JsonSerializerOptions { PropertyNameCaseInsensitive = true });

        Assert.Contains("dev stack check", problem!.Detail);
    }
}

