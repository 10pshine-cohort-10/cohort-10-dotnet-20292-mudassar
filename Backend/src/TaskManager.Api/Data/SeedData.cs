using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;
using TaskManager.Api.Entities;

namespace TaskManager.Api.Data;

public static class SeedData
{
    public static async Task InitializeAsync(IServiceProvider sp)
    {
        var db = sp.GetRequiredService<AppDbContext>();
        var log = sp.GetRequiredService<ILoggerFactory>().CreateLogger(nameof(SeedData));
        var config = sp.GetRequiredService<IConfiguration>();

        if (db.Database.IsRelational())
            await db.Database.MigrateAsync();
        else
            await db.Database.EnsureCreatedAsync();

        var roles = sp.GetRequiredService<RoleManager<IdentityRole<Guid>>>();
        foreach (var r in Roles.All)
            if (!await roles.RoleExistsAsync(r))
                await roles.CreateAsync(new IdentityRole<Guid>(r));

        var users = sp.GetRequiredService<UserManager<ApplicationUser>>();
        await SeedAccountAsync(users, config.GetSection("SeedAdmin"), Roles.Admin, "Admin", log);
        await SeedAccountAsync(users, config.GetSection("SeedUser"), Roles.User, "Test User", log);
    }

    private static async Task SeedAccountAsync(
        UserManager<ApplicationUser> users,
        IConfigurationSection cfg,
        string role,
        string defaultName,
        ILogger log)
    {
        var email = cfg["Email"];
        var password = cfg["Password"];
        if (string.IsNullOrWhiteSpace(email) || string.IsNullOrWhiteSpace(password))
            return;

        if (await users.FindByEmailAsync(email) is not null)
            return;

        var account = new ApplicationUser
        {
            UserName = email,
            Email = email,
            EmailConfirmed = true,
            FullName = cfg["FullName"] ?? defaultName
        };

        var result = await users.CreateAsync(account, password);
        if (result.Succeeded)
        {
            await users.AddToRoleAsync(account, role);
            log.LogInformation("Seeded {Role} account {UserId}", role, account.Id);
        }
        else
        {
            log.LogWarning("Could not seed {Role} account: {Errors}",
                role, string.Join(" ", result.Errors.Select(e => e.Description)));
        }
    }
}
