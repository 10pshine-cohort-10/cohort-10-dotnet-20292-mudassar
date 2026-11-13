using Microsoft.AspNetCore.Hosting;
using Microsoft.AspNetCore.Mvc.Testing;
using Microsoft.EntityFrameworkCore;
using Microsoft.Extensions.DependencyInjection;
using TaskManager.Api.Data;
using TaskManager.Tests.Helpers;

namespace TaskManager.Tests.Integration;

/// <summary>Runs the real pipeline against an in-memory database using credentials and JWT settings from `.env`.</summary>
public class TestApiFactory : WebApplicationFactory<Program>
{
    public string AdminEmail => TestEnv.AdminEmail;
    public string AdminPassword => TestEnv.AdminPassword;

    private readonly string _dbName = $"TaskManagerTests-{Guid.NewGuid()}";

    protected override void ConfigureWebHost(IWebHostBuilder builder)
    {
        builder.UseEnvironment("Testing");

        builder.UseSetting("Jwt:Issuer", TestEnv.JwtIssuer);
        builder.UseSetting("Jwt:Audience", TestEnv.JwtAudience);
        builder.UseSetting("Jwt:Key", TestEnv.JwtKey);
        builder.UseSetting("SeedAdmin:Email", AdminEmail);
        builder.UseSetting("SeedAdmin:Password", AdminPassword);
        builder.UseSetting("RateLimiting:AuthPermitLimit", "1000");

        builder.ConfigureServices(services =>
        {
            // Replace SQL Server with InMemory
            var toRemove = services.Where(d =>
                    d.ServiceType == typeof(DbContextOptions<AppDbContext>) ||
                    d.ServiceType == typeof(DbContextOptions) ||
                    (d.ServiceType.IsGenericType &&
                     d.ServiceType.GetGenericTypeDefinition().FullName == "Microsoft.EntityFrameworkCore.Infrastructure.IDbContextOptionsConfiguration`1"))
                .ToList();
            foreach (var d in toRemove) services.Remove(d);

            services.AddDbContext<AppDbContext>(o => o.UseInMemoryDatabase(_dbName));
        });
    }
}
