using System.Security.Claims;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TaskManager.Api.Data;
using TaskManager.Api.Extensions;
using TaskManager.Api.Middleware;

EnvFileLoader.Load();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console()
    .CreateLogger();

try
{
    Log.Information("Starting TaskManager API host");

    var builder = WebApplication.CreateBuilder(args);

    builder.Host.UseSerilog((ctx, sp, cfg) => cfg
        .ReadFrom.Configuration(ctx.Configuration)
        .ReadFrom.Services(sp)
        .Enrich.FromLogContext());

    // ---------- Services ----------
    builder.Services.AddControllers();
    builder.Services.AddSwaggerWithJwt();

    builder.Services.AddDbContext<AppDbContext>(o =>
        o.UseSqlServer(builder.Configuration.GetConnectionString("DefaultConnection")));

    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddPolicy("Frontend",
        p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

    builder.Services.AddAuth(builder.Configuration);

    // ---------- Pipeline ----------
    var app = builder.Build();

    app.UseMiddleware<GlobalExceptionMiddleware>();   // first, so it wraps everything
    app.UseSerilogRequestLogging(options =>           // one structured log line per HTTP request
    {
        options.EnrichDiagnosticContext = (diagnosticContext, httpContext) =>
        {
            var userId = httpContext.User.Identity?.IsAuthenticated == true
                ? httpContext.User.FindFirstValue(ClaimTypes.NameIdentifier) ?? "Unknown"
                : "Anonymous";
            diagnosticContext.Set("UserId", userId);
            diagnosticContext.Set("TraceId", httpContext.TraceIdentifier);
        };
    });

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

    app.UseHttpsRedirection();
    app.UseCors("Frontend");
    app.UseRateLimiter();
    app.UseAuthentication();                          // MUST come before UseAuthorization
    app.UseMiddleware<UserLogContextMiddleware>();    // pushes UserId into Serilog LogContext
    app.UseAuthorization();

    app.MapControllers();
    app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
       .AllowAnonymous()
       .WithName("Health");

    using (var scope = app.Services.CreateScope())
        await SeedData.InitializeAsync(scope.ServiceProvider);

    app.Run();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "TaskManager API host terminated unexpectedly");
    throw;
}
finally
{
    Log.CloseAndFlush();
}

public partial class Program { }   // needed for WebApplicationFactory in tests
