using System.Globalization;
using System.Security.Claims;
using System.Text.Json.Serialization;
using Serilog;
using TaskManager.Api.Extensions;
using TaskManager.Api.Middleware;

await EnvFileLoader.LoadAsync();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .Enrich.FromLogContext()
    .WriteTo.Async(
        a => a.Console(formatProvider: CultureInfo.InvariantCulture),
        bufferSize: 10000,
        blockWhenFull: false)
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
    builder.Services.AddEndpointsApiExplorer();
    builder.Services.AddSwaggerGen();

    var origins = builder.Configuration.GetSection("Cors:Origins").Get<string[]>() ?? [];
    builder.Services.AddCors(o => o.AddPolicy("Frontend",
        p => p.WithOrigins(origins).AllowAnyHeader().AllowAnyMethod()));

    // ---------- Pipeline ----------
    var app = builder.Build();

    app.UseHttpsRedirection();
    app.UseCors("Frontend");
    app.UseSerilogRequestLogging(options =>
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

    app.UseMiddleware<UserLogContextMiddleware>();

    app.MapControllers();
    app.MapGet("/health", (ILogger<Program> logger) =>
        {
            logger.LogDebug("Health check probe executed");
            return Results.Ok(new { status = "Healthy" });
        })
       .AllowAnonymous()
       .WithName("Health");

    await app.RunAsync();
}
catch (Exception ex) when (ex is not HostAbortedException)
{
    Log.Fatal(ex, "TaskManager API host terminated unexpectedly");
    throw;
}
finally
{
    await Log.CloseAndFlushAsync();
}

public partial class Program { }
