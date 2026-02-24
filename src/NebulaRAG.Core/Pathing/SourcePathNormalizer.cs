namespace NebulaRAG.Core.Pathing;

/// <summary>
/// Provides normalization helpers for source-path keys stored in the RAG index.
/// </summary>
public static class SourcePathNormalizer
{
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
        var normalizedProjectRoot = NormalizeAbsolutePath(projectRootPath);

        string normalizedPath;
        if (Path.IsPathRooted(trimmed))
        {
            normalizedPath = NormalizeAbsolutePath(trimmed);
            if (IsPathUnderRoot(normalizedPath, normalizedProjectRoot))
            {
                var relative = Path.GetRelativePath(normalizedProjectRoot, normalizedPath);
                return NormalizeRelativePath(relative);
            }

            var fallbackRelative = TryExtractRelativeByProjectFolderName(normalizedPath, normalizedProjectRoot);
            if (fallbackRelative is not null)
            {
                return fallbackRelative;
            }

            return normalizedPath;
        }

        return NormalizeRelativePath(trimmed);
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
}
