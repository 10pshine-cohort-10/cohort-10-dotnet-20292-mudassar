using Microsoft.AspNetCore.Identity;
using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
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
        {
            if (await roles.RoleExistsAsync(r))
                continue;

            var roleEntity = new IdentityRole<Guid>(r);
            IdentityResult roleResult;
            try
            {
                roleResult = await roles.CreateAsync(roleEntity);
            }
            catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
            {
                db.Entry(roleEntity).State = EntityState.Detached;
                if (await roles.RoleExistsAsync(r))
                {
                    log.LogInformation(ex, "Role {Role} was already seeded concurrently by another instance.", r);
                    continue;
                }

                throw;
            }

            if (!roleResult.Succeeded)
            {
                db.Entry(roleEntity).State = EntityState.Detached;
                if (await roles.RoleExistsAsync(r))
                {
                    log.LogInformation("Role {Role} was already seeded concurrently by another instance.", r);
                    continue;
                }

                throw new InvalidOperationException(
                    $"Could not seed role '{r}': {string.Join(" ", roleResult.Errors.Select(e => e.Description))}");
            }
        }

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

        var isAdminSeed = string.Equals(role, Roles.Admin, StringComparison.Ordinal);

        var existingAccount = await users.FindByEmailAsync(email);
        if (existingAccount is not null)
        {
            await ReportAdminSeedConflictIfNeededAsync(users, existingAccount, isAdminSeed, log);
            return;
        }

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

        var createResult = await TryCreateAccountAsync(db, tx, users, account, password, role, log);
        if (createResult is null)
            return;

        if (!createResult.Succeeded)
        {
            await RollbackAsync(tx);
            db.Entry(account).State = EntityState.Detached;
            await HandleCreateAccountFailureAsync(users, createResult, email, role, isAdminSeed, log);
            return;
        }

        var roleResult = await users.AddToRoleAsync(account, role);
        if (!roleResult.Succeeded)
        {
            await HandleAssignRoleFailureAsync(tx, users, account, roleResult, role, isAdminSeed, log);
            return;
        }

        if (tx is not null)
            await tx.CommitAsync();

        log.LogInformation("Seeded {Role} account {UserId}", role, account.Id);
    }

    private static async Task<IdentityResult?> TryCreateAccountAsync(
        AppDbContext db,
        IDbContextTransaction? tx,
        UserManager<ApplicationUser> users,
        ApplicationUser account,
        string password,
        string role,
        ILogger log)
    {
        try
        {
            return await users.CreateAsync(account, password);
        }
        catch (DbUpdateException ex) when (IsUniqueConstraintViolation(ex))
        {
            await RollbackAsync(tx);
            db.Entry(account).State = EntityState.Detached;
            log.LogInformation(ex, "{Role} account was already seeded concurrently by another instance.", role);
            return null;
        }
    }

    private static async Task HandleCreateAccountFailureAsync(
        UserManager<ApplicationUser> users,
        IdentityResult createResult,
        string email,
        string role,
        bool isAdminSeed,
        ILogger log)
    {
        var createErrors = string.Join(" ", createResult.Errors.Select(e => e.Description));
        if (isAdminSeed)
        {
            var existingAfterFailure = await users.FindByEmailAsync(email);
            if (existingAfterFailure is not null)
            {
                await ReportAdminSeedConflictIfNeededAsync(users, existingAfterFailure, isAdminSeed, log);
                return;
            }

            throw new InvalidOperationException($"Could not seed the configured {role} account: {createErrors}");
        }

        log.LogWarning("Could not seed {Role} account: {Errors}", role, createErrors);
    }

    private static async Task HandleAssignRoleFailureAsync(
        IDbContextTransaction? tx,
        UserManager<ApplicationUser> users,
        ApplicationUser account,
        IdentityResult roleResult,
        string role,
        bool isAdminSeed,
        ILogger log)
    {
        if (tx is not null)
            await tx.RollbackAsync();
        else
            await users.DeleteAsync(account);

        var roleErrors = string.Join(" ", roleResult.Errors.Select(e => e.Description));
        if (isAdminSeed)
            throw new InvalidOperationException(
                $"Could not assign the {role} role to the configured seed account: {roleErrors}");

        log.LogWarning("Could not assign {Role} role to seeded account {UserId}: {Errors}",
            role, account.Id, roleErrors);
    }

    private static async Task RollbackAsync(IDbContextTransaction? tx)
    {
        if (tx is not null)
            await tx.RollbackAsync();
    }

    private static async Task ReportAdminSeedConflictIfNeededAsync(
        UserManager<ApplicationUser> users,
        ApplicationUser existingAccount,
        bool isAdminSeed,
        ILogger log)
    {
        if (isAdminSeed
            && (!existingAccount.IsActive || !await users.IsInRoleAsync(existingAccount, Roles.Admin)))
        {
            // Report the conflict but never promote an existing non-admin (or inactive) account automatically.
            log.LogError(
                "SeedAdmin configuration conflict: account {UserId} already exists for the configured admin email but is inactive or does not hold the {Role} role. Skipping automatic promotion.",
                existingAccount.Id, Roles.Admin);
        }
    }
}
