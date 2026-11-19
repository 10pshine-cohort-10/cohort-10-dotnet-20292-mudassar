using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Microsoft.EntityFrameworkCore;
using Serilog;
using TaskManager.Api.Data;
using TaskManager.Api.Extensions;
using TaskManager.Api.Middleware;

EnvFileLoader.Load();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Console(formatProvider: CultureInfo.InvariantCulture)
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
    builder.Services.AddControllers()
        .AddJsonOptions(o => o.JsonSerializerOptions.Converters.Add(new JsonStringEnumConverter()));
    builder.Services.AddSwaggerWithJwt();

    builder.Services.AddDbContext<AppDbContext>(o =>
    {
        var connectionString = builder.Configuration.GetConnectionString("DefaultConnection");
        if (string.IsNullOrWhiteSpace(connectionString))
            throw new InvalidOperationException(
                "ConnectionStrings:DefaultConnection is missing or empty. Set it in .env, user-secrets, or appsettings.");
        o.UseSqlServer(connectionString);
    });

    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddPolicy("Frontend",
        p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

    builder.Services.AddAuth(builder.Configuration);

    // ---------- Pipeline ----------
    var app = builder.Build();

    app.UseHttpsRedirection();
    app.UseCors("Frontend");                          // before exception & auth middleware so error responses keep CORS headers
    app.UseSerilogRequestLogging(options =>           // before GlobalExceptionMiddleware so it observes final handled status codes
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
    app.UseMiddleware<GlobalExceptionMiddleware>();   // wraps downstream pipeline in ProblemDetails

    if (app.Environment.IsDevelopment())
    {
        app.UseSwagger();
        app.UseSwaggerUI();
    }

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
