namespace TaskManager.Api.Extensions;

/// <summary>
/// Loads key=value pairs from the nearest `.env` file into process environment variables
/// without overwriting variables that are already set.
/// </summary>
public static class EnvFileLoader
{
    public static void Load()
    {
        var envPath = FindEnvFile(Directory.GetCurrentDirectory())
                      ?? FindEnvFile(AppContext.BaseDirectory);
        if (envPath is null)
            return;

        foreach (var rawLine in File.ReadAllLines(envPath))
        {
            var line = rawLine.Trim();
            if (line.Length == 0 || line.StartsWith('#'))
                continue;

            var sep = line.IndexOf('=');
            if (sep <= 0)
                continue;

            var key = line[..sep].Trim();
            var value = line[(sep + 1)..].Trim();

            if (value.Length >= 2 &&
                ((value.StartsWith('"') && value.EndsWith('"')) ||
                 (value.StartsWith('\'') && value.EndsWith('\''))))
            {
                value = value[1..^1];
            }

            if (string.IsNullOrEmpty(Environment.GetEnvironmentVariable(key)))
                Environment.SetEnvironmentVariable(key, value);
        }
    }

    private static string? FindEnvFile(string startDirectory)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            if (File.Exists(candidate))
                return candidate;
        }
        return null;
    }
}
