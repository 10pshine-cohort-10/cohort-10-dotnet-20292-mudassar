using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Data;
using TaskManager.Api.Extensions;
using TaskManager.Api.Middleware;

EnvFileLoader.Load();

var builder = WebApplication.CreateBuilder(args);

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
// app.UseSerilogRequestLogging();                // added in feature/be-logging-serilog

if (app.Environment.IsDevelopment())
{
    app.UseSwagger();
    app.UseSwaggerUI();
}

app.UseHttpsRedirection();
app.UseCors("Frontend");
app.UseRateLimiter();
app.UseAuthentication();                          // MUST come before UseAuthorization
app.UseAuthorization();

app.MapControllers();
app.MapGet("/health", () => Results.Ok(new { status = "Healthy" }))
   .AllowAnonymous()
   .WithName("Health");

using (var scope = app.Services.CreateScope())
    await SeedData.InitializeAsync(scope.ServiceProvider);

app.Run();

public partial class Program { }   // needed for WebApplicationFactory in tests
