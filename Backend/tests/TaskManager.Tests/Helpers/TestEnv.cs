using TaskManager.Api.Extensions;

namespace TaskManager.Tests.Helpers;

/// <summary>
/// Loads secrets and test credentials from `.env` (or environment variables) so no secrets are hardcoded in tests.
/// </summary>
public static class TestEnv
{
    static TestEnv() => EnvFileLoader.Load();

    public static string JwtIssuer => Get("Jwt__Issuer", "TaskManager");
    public static string JwtAudience => Get("Jwt__Audience", "TaskManagerClient");
    public static string JwtKey => Require("Jwt__Key");
    public static int JwtExpiryMinutes => int.TryParse(Environment.GetEnvironmentVariable("Jwt__ExpiryMinutes"), out var m) ? m : 60;

    public static string AdminEmail => Require("SeedAdmin__Email");
    public static string AdminPassword => Require("SeedAdmin__Password");

    public static string UserEmail => Require("SeedUser__Email");
    public static string UserName => Get("SeedUser__FullName", "Test User");
    public static string UserPassword => Require("SeedUser__Password");

    private static string Get(string key, string fallback) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value ? value : fallback;

    private static string Require(string key) =>
        Environment.GetEnvironmentVariable(key) is { Length: > 0 } value
            ? value
            : throw new InvalidOperationException(
                $"Required environment variable '{key}' is missing. Ensure `.env` exists at the repository root (copy `.env.example`).");
}

