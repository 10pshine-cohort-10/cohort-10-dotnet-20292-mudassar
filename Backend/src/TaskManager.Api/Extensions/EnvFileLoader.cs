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

        ApplyLines(File.ReadAllLines(envPath));
    }

    public static async Task LoadAsync(CancellationToken cancellationToken = default)
    {
        var lines = await ReadFirstEnvFileAsync(Directory.GetCurrentDirectory(), cancellationToken)
                    ?? await ReadFirstEnvFileAsync(AppContext.BaseDirectory, cancellationToken);
        if (lines is null)
            return;

        ApplyLines(lines);
    }

    private static async Task<string[]?> ReadFirstEnvFileAsync(
        string startDirectory,
        CancellationToken cancellationToken)
    {
        for (var dir = new DirectoryInfo(startDirectory); dir is not null; dir = dir.Parent)
        {
            var candidate = Path.Combine(dir.FullName, ".env");
            try
            {
                return await File.ReadAllLinesAsync(candidate, cancellationToken);
            }
            catch (FileNotFoundException)
            {
                // Continue searching parent directories.
            }
            catch (DirectoryNotFoundException)
            {
                // Continue searching parent directories.
            }
        }
        return null;
    }

    private static void ApplyLines(string[] lines)
    {
        foreach (var rawLine in lines)
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

            if (Environment.GetEnvironmentVariable(key) is null)
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
