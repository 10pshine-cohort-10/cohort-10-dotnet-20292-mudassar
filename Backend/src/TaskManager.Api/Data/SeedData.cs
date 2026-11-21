using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
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
        await SeedAccountAsync(db, users, config.GetSection("SeedAdmin"), Roles.Admin, "Admin", log);
        await SeedAccountAsync(db, users, config.GetSection("SeedUser"), Roles.User, "Test User", log);

        await SeedDefaultCategoriesAsync(db, log);
    }

    private static async Task SeedDefaultCategoriesAsync(AppDbContext db, ILogger log)
    {
        if (await db.Categories.AnyAsync())
            return;

        db.Categories.AddRange(
            new Category { Name = "Work", Description = "Work and project deliverables" },
            new Category { Name = "Personal", Description = "Personal errands and goals" },
            new Category { Name = "Urgent", Description = "Time-sensitive items requiring immediate attention" });

        try
        {
            await db.SaveChangesAsync();
            log.LogInformation("Seeded default categories (Work, Personal, Urgent)");
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            log.LogInformation(ex, "Default categories were already seeded concurrently by another instance.");
        }
    }

    private static bool IsUniqueConstraintViolation(DbUpdateException ex)
    {
        if (ex.InnerException is SqlException sqlEx && sqlEx.Number is 2601 or 2627)
            return true;

        var inner = ex.InnerException;
        return inner is not null
            && string.Equals(inner.GetType().Name, "SqliteException", StringComparison.Ordinal)
            && inner.Message.Contains("UNIQUE", StringComparison.OrdinalIgnoreCase);
    }

    private static async Task SeedAccountAsync(
        AppDbContext db,
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

        await using var tx = db.Database.IsRelational()
            ? await db.Database.BeginTransactionAsync()
            : null;

        IdentityResult createResult;
        try
        {
            createResult = await users.CreateAsync(account, password);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            if (tx is not null)
                await tx.RollbackAsync();

            db.Entry(account).State = EntityState.Detached;
            log.LogInformation(ex, "{Role} account was already seeded concurrently by another instance.", role);
            return;
        }

        if (!createResult.Succeeded)
        {
            if (tx is not null)
                await tx.RollbackAsync();

            log.LogWarning("Could not seed {Role} account: {Errors}",
                role, string.Join(" ", createResult.Errors.Select(e => e.Description)));
            return;
        }

        var roleResult = await users.AddToRoleAsync(account, role);
        if (!roleResult.Succeeded)
        {
            if (tx is not null)
                await tx.RollbackAsync();
            else
                await users.DeleteAsync(account);

            log.LogWarning("Could not assign {Role} role to seeded account {UserId}: {Errors}",
                role, account.Id, string.Join(" ", roleResult.Errors.Select(e => e.Description)));
            return;
        }

        if (tx is not null)
            await tx.CommitAsync();

        log.LogInformation("Seeded {Role} account {UserId}", role, account.Id);
    }
}
