namespace NebulaRAG.Core.Pathing;

/// <summary>
/// Provides normalization helpers for source-path keys stored in the RAG index.
/// </summary>
public static class SourcePathNormalizer
{
    private static readonly HashSet<string> GenericProjectFolderNames = new(StringComparer.OrdinalIgnoreCase)
    {
        "app",
        "workspace",
        "work",
        "src",
        "repo",
        "project",
        "projects",
        "code"
    };

    /// <summary>
    /// Normalizes a source path to a stable storage key.
    /// </summary>
    /// <param name="sourcePath">Original source path from caller.</param>
    /// <param name="projectRootPath">Project root path used for relative key conversion.</param>
    /// <returns>Normalized source key with forward slashes and no leading './'.</returns>
    public static string NormalizeForStorage(string sourcePath, string projectRootPath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return sourcePath;
        }

        var trimmed = sourcePath.Trim();
        if (Uri.TryCreate(trimmed, UriKind.Absolute, out var absoluteUri) && !absoluteUri.IsFile)
        {
            return trimmed;
        }

        var normalizedProjectRoot = NormalizeAbsolutePath(projectRootPath);
        var projectFolderName = Path.GetFileName(normalizedProjectRoot.TrimEnd('/'));

        string normalizedPath;
        if (Path.IsPathRooted(trimmed))
        {
            normalizedPath = NormalizeAbsolutePath(trimmed);
            if (IsPathUnderRoot(normalizedPath, normalizedProjectRoot))
            {
                var relative = Path.GetRelativePath(normalizedProjectRoot, normalizedPath);
                return PrefixWithProjectFolder(NormalizeRelativePath(relative), projectFolderName);
            }

            var fallbackRelative = TryExtractRelativeByProjectFolderName(normalizedPath, normalizedProjectRoot);
            if (fallbackRelative is not null)
            {
                return PrefixWithProjectFolder(fallbackRelative, projectFolderName);
            }

            return normalizedPath;
        }

        return PrefixWithProjectFolder(NormalizeRelativePath(trimmed), projectFolderName);
    }

    /// <summary>
    /// Applies an explicit project-name prefix to a normalized storage key.
    /// </summary>
    /// <param name="normalizedSourcePath">Already-normalized source path key.</param>
    /// <param name="projectName">Optional explicit project name from caller input.</param>
    /// <returns>Prefixed key when projectName is valid; otherwise original key.</returns>
    public static string ApplyExplicitProjectPrefix(string normalizedSourcePath, string? projectName)
    {
        if (string.IsNullOrWhiteSpace(normalizedSourcePath))
        {
            return normalizedSourcePath;
        }

        var normalizedProjectName = NormalizeProjectName(projectName);
        if (string.IsNullOrWhiteSpace(normalizedProjectName))
        {
            return normalizedSourcePath;
        }

        var projectPrefix = $"{normalizedProjectName}/";
        if (normalizedSourcePath.Equals(normalizedProjectName, StringComparison.OrdinalIgnoreCase)
            || normalizedSourcePath.StartsWith(projectPrefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedSourcePath;
        }

        return $"{normalizedProjectName}/{normalizedSourcePath}";
    }

    /// <summary>
    /// Returns true when an absolute path is located under a specific root.
    /// </summary>
    /// <param name="absolutePath">Absolute path to test.</param>
    /// <param name="absoluteRootPath">Absolute root path boundary.</param>
    /// <returns>True when path belongs to root; otherwise false.</returns>
    public static bool IsPathUnderRoot(string absolutePath, string absoluteRootPath)
    {
        if (string.IsNullOrWhiteSpace(absolutePath) || string.IsNullOrWhiteSpace(absoluteRootPath))
        {
            return false;
        }

        var normalizedPath = NormalizeAbsolutePath(absolutePath).TrimEnd('/');
        var normalizedRoot = NormalizeAbsolutePath(absoluteRootPath).TrimEnd('/');

        if (!normalizedPath.StartsWith(normalizedRoot, StringComparison.OrdinalIgnoreCase))
        {
            return false;
        }

        return normalizedPath.Length == normalizedRoot.Length || normalizedPath[normalizedRoot.Length] == '/';
    }

    /// <summary>
    /// Normalizes an absolute path with forward slashes.
    /// </summary>
    /// <param name="absolutePath">Absolute path to normalize.</param>
    /// <returns>Normalized absolute path.</returns>
    public static string NormalizeAbsolutePath(string absolutePath)
    {
        var fullPath = Path.GetFullPath(absolutePath);
        return fullPath.Replace('\\', '/');
    }

    /// <summary>
    /// Normalizes a relative path key with forward slashes.
    /// </summary>
    /// <param name="relativePath">Relative path to normalize.</param>
    /// <returns>Normalized relative key.</returns>
    public static string NormalizeRelativePath(string relativePath)
    {
        var normalized = relativePath.Replace('\\', '/');
        if (normalized.StartsWith("./", StringComparison.Ordinal))
        {
            normalized = normalized[2..];
        }

        return normalized;
    }

    /// <summary>
    /// Tries to derive a relative path by locating the repository folder name in the absolute path.
    /// </summary>
    /// <param name="normalizedAbsolutePath">Normalized absolute source path.</param>
    /// <param name="normalizedProjectRoot">Normalized project root path.</param>
    /// <returns>Relative path when the project folder segment is found; otherwise null.</returns>
    private static string? TryExtractRelativeByProjectFolderName(string normalizedAbsolutePath, string normalizedProjectRoot)
    {
        var projectFolderName = Path.GetFileName(normalizedProjectRoot.TrimEnd('/'));
        if (string.IsNullOrWhiteSpace(projectFolderName))
        {
            return null;
        }

        var marker = $"/{projectFolderName}/";
        var markerIndex = normalizedAbsolutePath.IndexOf(marker, StringComparison.OrdinalIgnoreCase);
        if (markerIndex < 0)
        {
            return null;
        }

        var relativeStart = markerIndex + marker.Length;
        if (relativeStart >= normalizedAbsolutePath.Length)
        {
            return null;
        }

        var relativePath = normalizedAbsolutePath[relativeStart..];
        return NormalizeRelativePath(relativePath);
    }

    /// <summary>
    /// Prefixes a relative path with the project folder name when missing.
    /// </summary>
    /// <param name="normalizedRelativePath">Normalized relative path.</param>
    /// <param name="projectFolderName">Project folder segment.</param>
    /// <returns>Prefixed relative path.</returns>
    private static string PrefixWithProjectFolder(string normalizedRelativePath, string? projectFolderName)
    {
        if (string.IsNullOrWhiteSpace(projectFolderName) || string.IsNullOrWhiteSpace(normalizedRelativePath))
        {
            return normalizedRelativePath;
        }

        // Avoid generic runtime working-directory prefixes (for example /app in containers).
        if (GenericProjectFolderNames.Contains(projectFolderName))
        {
            return normalizedRelativePath;
        }

        var prefix = $"{projectFolderName}/";
        if (normalizedRelativePath.Equals(projectFolderName, StringComparison.OrdinalIgnoreCase)
            || normalizedRelativePath.StartsWith(prefix, StringComparison.OrdinalIgnoreCase))
        {
            return normalizedRelativePath;
        }

        return $"{projectFolderName}/{normalizedRelativePath}";
    }

    /// <summary>
    /// Normalizes a user-provided project name to a safe source-key segment.
    /// </summary>
    /// <param name="projectName">Raw project name.</param>
    /// <returns>Normalized project segment without path separators.</returns>
    private static string NormalizeProjectName(string? projectName)
    {
        if (string.IsNullOrWhiteSpace(projectName))
        {
            return string.Empty;
        }

        var normalized = projectName.Trim().Replace('\\', '/').Trim('/');
        if (string.IsNullOrWhiteSpace(normalized))
        {
            return string.Empty;
        }

        return normalized;
    }
}
