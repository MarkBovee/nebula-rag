using NebulaRAG.Core.Services;

namespace NebulaRAG.Tests;

/// <summary>
/// Unit tests for root-level WSL/MSBuild artifact detection and cleanup.
/// </summary>
public sealed class BuildArtifactCleanupServiceTests : IDisposable
{
    private readonly string _rootPath = Path.Combine(Path.GetTempPath(), $"nebula-cleanup-tests-{Guid.NewGuid():N}");

    /// <summary>
    /// Initializes the temp repository root used by the test case.
    /// </summary>
    public BuildArtifactCleanupServiceTests()
    {
        Directory.CreateDirectory(_rootPath);
    }

    /// <summary>
    /// Verifies artifact discovery includes MSBuild temp folders and empty short random directories.
    /// </summary>
    [Fact]
    public void GetCleanupCandidates_ReturnsOnlyKnownArtifactPatterns()
    {
        Directory.CreateDirectory(Path.Combine(_rootPath, "MSBuildTempabc123"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "dW9wiU"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "src"));
        Directory.CreateDirectory(Path.Combine(_rootPath, "designs"));
        var nonEmptyRandomDirectory = Directory.CreateDirectory(Path.Combine(_rootPath, "ABC123"));
        File.WriteAllText(Path.Combine(nonEmptyRandomDirectory.FullName, "keep.txt"), "keep");

        var service = new BuildArtifactCleanupService();

        var candidates = service.GetCleanupCandidates(_rootPath);

        Assert.Equal(
            ["dW9wiU", "MSBuildTempabc123"],
            candidates.Select(candidate => candidate.DirectoryName).OrderBy(name => name).ToArray());
    }

    /// <summary>
    /// Verifies cleanup removes only the discovered artifact directories.
    /// </summary>
    [Fact]
    public void CleanupCandidates_RemovesArtifactsButKeepsRealDirectories()
    {
        var msbuildTempPath = Path.Combine(_rootPath, "MSBuildTempkeepmegone");
        var randomArtifactPath = Path.Combine(_rootPath, "xQAhYO");
        var sourcePath = Path.Combine(_rootPath, "src");
        var nonEmptyRandomDirectoryPath = Path.Combine(_rootPath, "Y3ZuEl");

        Directory.CreateDirectory(msbuildTempPath);
        Directory.CreateDirectory(randomArtifactPath);
        Directory.CreateDirectory(sourcePath);
        Directory.CreateDirectory(nonEmptyRandomDirectoryPath);
        File.WriteAllText(Path.Combine(nonEmptyRandomDirectoryPath, "project-file.txt"), "still here");

        var service = new BuildArtifactCleanupService();

        var removedCount = service.CleanupCandidates(_rootPath);

        Assert.Equal(2, removedCount);
        Assert.False(Directory.Exists(msbuildTempPath));
        Assert.False(Directory.Exists(randomArtifactPath));
        Assert.True(Directory.Exists(sourcePath));
        Assert.True(Directory.Exists(nonEmptyRandomDirectoryPath));
    }

    /// <summary>
    /// Removes the temp repository root created for the test case.
    /// </summary>
    public void Dispose()
    {
        if (Directory.Exists(_rootPath))
        {
            Directory.Delete(_rootPath, recursive: true);
        }
    }
}
