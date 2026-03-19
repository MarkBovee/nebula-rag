using System.Text;

namespace NebulaRAG.Core.Configuration;

/// <summary>
/// Loads .env files into process environment variables.
/// </summary>
public static class DotEnvLoader
{
    /// <summary>
    /// Loads the nearest .env file discovered from the start directory and app base directory.
    /// </summary>
    /// <param name="startDirectory">Optional start directory. Defaults to current directory.</param>
    /// <param name="overwriteExisting">When true, overwrites existing process environment values.</param>
    /// <returns>Result details including source path and variable counts.</returns>
    public static DotEnvLoadResult LoadStandardDotEnv(string? startDirectory = null, bool overwriteExisting = false)
    {
        var envPath = FindStandardDotEnvPath(startDirectory);
        if (string.IsNullOrWhiteSpace(envPath) || !File.Exists(envPath))
        {
            return DotEnvLoadResult.NoneFound();
        }

        return LoadFromFile(envPath, overwriteExisting);
    }

    /// <summary>
    /// Loads key/value entries from a specific .env file.
    /// </summary>
    /// <param name="envFilePath">Absolute or relative path to the .env file.</param>
    /// <param name="overwriteExisting">When true, overwrites existing process environment values.</param>
    /// <returns>Result details including applied variable counts.</returns>
    public static DotEnvLoadResult LoadFromFile(string envFilePath, bool overwriteExisting = false)
    {
        var resolvedPath = Path.GetFullPath(envFilePath);
        var loadedCount = 0;
        var skippedCount = 0;

        foreach (var line in File.ReadLines(resolvedPath, Encoding.UTF8))
        {
            if (!TryParseEntry(line, out var key, out var value))
            {
                continue;
            }

            var existingValue = Environment.GetEnvironmentVariable(key);
            if (!overwriteExisting && !string.IsNullOrEmpty(existingValue))
            {
                skippedCount++;
                continue;
            }

            Environment.SetEnvironmentVariable(key, value);
            loadedCount++;
        }

        return new DotEnvLoadResult(true, resolvedPath, loadedCount, skippedCount);
    }

    /// <summary>
    /// Finds the nearest .env file by walking parent directories from the start directory.
    /// Falls back to app base directory when needed.
    /// </summary>
    /// <param name="startDirectory">Optional start directory. Defaults to current directory.</param>
    /// <returns>Resolved .env path when found; otherwise null.</returns>
    private static string? FindStandardDotEnvPath(string? startDirectory)
    {
        var directory = Path.GetFullPath(string.IsNullOrWhiteSpace(startDirectory)
            ? Directory.GetCurrentDirectory()
            : startDirectory);

        var visited = new HashSet<string>(StringComparer.OrdinalIgnoreCase);
        var current = new DirectoryInfo(directory);
        while (current is not null)
        {
            if (visited.Add(current.FullName))
            {
                var envPath = Path.Combine(current.FullName, ".env");
                if (File.Exists(envPath))
                {
                    return envPath;
                }
            }

            current = current.Parent;
        }

        var appBaseDirectory = Path.GetFullPath(AppContext.BaseDirectory);
        var appBaseEnvPath = Path.Combine(appBaseDirectory, ".env");
        if (File.Exists(appBaseEnvPath))
        {
            return appBaseEnvPath;
        }

        return null;
    }

    /// <summary>
    /// Parses one .env entry line into key and value.
    /// </summary>
    /// <param name="line">Input line.</param>
    /// <param name="key">Parsed key when successful.</param>
    /// <param name="value">Parsed value when successful.</param>
    /// <returns>True when a valid entry is parsed; otherwise false.</returns>
    private static bool TryParseEntry(string line, out string key, out string value)
    {
        key = string.Empty;
        value = string.Empty;

        if (string.IsNullOrWhiteSpace(line))
        {
            return false;
        }

        var trimmed = line.Trim();
        if (trimmed.StartsWith('#'))
        {
            return false;
        }

        if (trimmed.StartsWith("export ", StringComparison.Ordinal))
        {
            trimmed = trimmed[7..].TrimStart();
        }

        var separatorIndex = trimmed.IndexOf('=');
        if (separatorIndex <= 0)
        {
            return false;
        }

        key = trimmed[..separatorIndex].Trim();
        if (string.IsNullOrWhiteSpace(key))
        {
            return false;
        }

        var rawValue = trimmed[(separatorIndex + 1)..].Trim();
        value = TrimQuotes(rawValue);
        return true;
    }

    /// <summary>
    /// Removes matching wrapping single or double quotes from a value.
    /// </summary>
    /// <param name="value">Raw value.</param>
    /// <returns>Unquoted value.</returns>
    private static string TrimQuotes(string value)
    {
        if (value.Length < 2)
        {
            return value;
        }

        if ((value.StartsWith('"') && value.EndsWith('"')) || (value.StartsWith('\'') && value.EndsWith('\'')))
        {
            return value[1..^1];
        }

        return value;
    }
}

/// <summary>
/// Result details for .env loading operations.
/// </summary>
/// <param name="FoundFile">Whether an env file was found.</param>
/// <param name="Path">Resolved env file path when found.</param>
/// <param name="LoadedCount">Count of variables applied to process environment.</param>
/// <param name="SkippedCount">Count of variables skipped due to existing values.</param>
public sealed record DotEnvLoadResult(bool FoundFile, string? Path, int LoadedCount, int SkippedCount)
{
    /// <summary>
    /// Creates a result indicating no file was found.
    /// </summary>
    /// <returns>Empty result.</returns>
    public static DotEnvLoadResult NoneFound()
    {
        return new DotEnvLoadResult(false, null, 0, 0);
    }
}
