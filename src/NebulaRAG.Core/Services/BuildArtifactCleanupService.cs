using System.Text.RegularExpressions;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Identifies and removes root-level build artifact directories that WSL-mounted builds can leave behind.
/// </summary>
public sealed class BuildArtifactCleanupService
{
    private static readonly Regex RandomArtifactDirectoryNamePattern = new("^[A-Za-z0-9]{6}$", RegexOptions.Compiled);

    /// <summary>
    /// Returns cleanup candidates for the provided repository root.
    /// </summary>
    /// <param name="repositoryRoot">Repository root path to inspect.</param>
    /// <returns>Directories that match the known transient artifact patterns.</returns>
    public IReadOnlyList<BuildArtifactCleanupCandidate> GetCleanupCandidates(string repositoryRoot)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(repositoryRoot);

        if (!Directory.Exists(repositoryRoot))
        {
            throw new DirectoryNotFoundException($"Repository root was not found: {repositoryRoot}");
        }

        var candidates = new List<BuildArtifactCleanupCandidate>();
        foreach (var directoryPath in Directory.EnumerateDirectories(repositoryRoot, "*", SearchOption.TopDirectoryOnly))
        {
            var directoryName = Path.GetFileName(directoryPath);
            if (string.IsNullOrWhiteSpace(directoryName))
            {
                continue;
            }

            if (directoryName.StartsWith("MSBuildTemp", StringComparison.Ordinal))
            {
                candidates.Add(new BuildArtifactCleanupCandidate(directoryName, directoryPath, "msbuild-temp"));
                continue;
            }

            // WSL-mounted .NET builds can leave empty six-character alphanumeric directories in the repo root.
            if (RandomArtifactDirectoryNamePattern.IsMatch(directoryName) && IsEmptyDirectory(directoryPath))
            {
                candidates.Add(new BuildArtifactCleanupCandidate(directoryName, directoryPath, "empty-random"));
            }
        }

        return candidates;
    }

    /// <summary>
    /// Deletes all cleanup candidates discovered under the provided repository root.
    /// </summary>
    /// <param name="repositoryRoot">Repository root path to clean.</param>
    /// <returns>The number of directories removed.</returns>
    public int CleanupCandidates(string repositoryRoot)
    {
        var candidates = GetCleanupCandidates(repositoryRoot);
        foreach (var candidate in candidates)
        {
            Directory.Delete(candidate.FullPath, recursive: true);
        }

        return candidates.Count;
    }

    /// <summary>
    /// Returns whether a directory contains any entries.
    /// </summary>
    /// <param name="directoryPath">Directory path to inspect.</param>
    /// <returns><c>true</c> when the directory is empty.</returns>
    private static bool IsEmptyDirectory(string directoryPath)
    {
        return !Directory.EnumerateFileSystemEntries(directoryPath).Any();
    }
}

/// <summary>
/// Describes a root-level build artifact directory selected for cleanup.
/// </summary>
/// <param name="DirectoryName">Directory name found under the repository root.</param>
/// <param name="FullPath">Absolute directory path.</param>
/// <param name="Kind">Artifact classification used for reporting.</param>
public sealed record BuildArtifactCleanupCandidate(string DirectoryName, string FullPath, string Kind);
