using System.Text.Json;
using NebulaRAG.Core.Models;

namespace NebulaRAG.Tests;

public sealed class SyncModelsTests
{
    [Fact]
    public void SyncSummary_RoundTrips_ThroughJson()
    {
        var summary = new SyncSummary(3, 1, 2, ["err1", "err2"], 420L);
        var json = JsonSerializer.Serialize(summary);
        var deserialized = JsonSerializer.Deserialize<SyncSummary>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.FilesIngested);
        Assert.Equal(1, deserialized.MemoriesPruned);
        Assert.Equal(2, deserialized.SourcesReindexed);
        Assert.Equal(420L, deserialized.DurationMs);
        Assert.Equal(new[] { "err1", "err2" }, deserialized.Errors);
    }
}
