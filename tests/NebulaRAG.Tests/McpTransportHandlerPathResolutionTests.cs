namespace NebulaRAG.Tests;

public sealed class McpTransportHandlerPathResolutionTests
{
    /// <summary>
    /// Windows absolute paths should be rejected on non-Windows hosts before path normalization mangles them.
    /// </summary>
    [Fact]
    public void TryResolveIngestPath_WindowsAbsolutePathOnNonWindows_ReturnsClearError()
    {
        var supported = OperatingSystem.IsWindows();
        var resolved = string.Empty;

        var success = NebulaRAG.Core.Mcp.McpTransportHandler.TryResolveIngestPath(@"E:\Projects\Repo", out resolved, out var errorMessage);

        if (supported)
        {
            Assert.True(success);
            Assert.Null(errorMessage);
            return;
        }

        Assert.False(success);
        Assert.Equal(@"E:\Projects\Repo", resolved);
        Assert.NotNull(errorMessage);
        Assert.Contains("Windows absolute paths are not supported", errorMessage, StringComparison.OrdinalIgnoreCase);
    }

    /// <summary>
    /// Directory ingests that index nothing should be surfaced as validation failures.
    /// </summary>
    [Fact]
    public void ShouldRejectNoOpDirectoryIngest_WhenDirectoryIndexesNothing_ReturnsTrue()
    {
        var summary = new NebulaRAG.Core.Services.IndexSummary
        {
            DocumentsIndexed = 0,
            DocumentsSkipped = 5,
            ChunksIndexed = 0
        };

        Assert.True(NebulaRAG.Core.Mcp.McpTransportHandler.ShouldRejectNoOpDirectoryIngest(true, summary));
        Assert.False(NebulaRAG.Core.Mcp.McpTransportHandler.ShouldRejectNoOpDirectoryIngest(false, summary));
        Assert.False(NebulaRAG.Core.Mcp.McpTransportHandler.ShouldRejectNoOpDirectoryIngest(true, new NebulaRAG.Core.Services.IndexSummary { DocumentsIndexed = 1 }));
    }
}
