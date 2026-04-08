using System.Text.Json;
using NebulaRAG.Core.Models;

namespace NebulaRAG.Tests;

public sealed class SyncModelsTests
{
    [Fact]
    public void SyncSummary_RoundTrips_ThroughJson()
    {
        var summary = new SyncSummary(3, 1, 2, 4, ["err1", "err2"], 420L);
        var json = JsonSerializer.Serialize(summary);
        var deserialized = JsonSerializer.Deserialize<SyncSummary>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(3, deserialized.FilesIngested);
        Assert.Equal(1, deserialized.MemoriesPruned);
        Assert.Equal(2, deserialized.SourcesPruned);
        Assert.Equal(4, deserialized.SourcesReindexed);
        Assert.Equal(420L, deserialized.DurationMs);
        Assert.Equal(new[] { "err1", "err2" }, deserialized.Errors);
    }

    [Fact]
    public void SyncSummary_EmptyErrors_RoundTrips()
    {
        var summary = new SyncSummary(0, 0, 0, 0, [], 0L);
        var json = JsonSerializer.Serialize(summary);
        var deserialized = JsonSerializer.Deserialize<SyncSummary>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(0, deserialized.FilesIngested);
        Assert.Equal(0, deserialized.MemoriesPruned);
        Assert.Equal(0, deserialized.SourcesPruned);
        Assert.Equal(0, deserialized.SourcesReindexed);
        Assert.Equal(0L, deserialized.DurationMs);
        Assert.Empty(deserialized.Errors);
    }

    [Fact]
    public void SyncSummary_LargeDurationMs_RoundTrips()
    {
        var summary = new SyncSummary(0, 0, 0, 0, [], long.MaxValue);
        var json = JsonSerializer.Serialize(summary);
        var deserialized = JsonSerializer.Deserialize<SyncSummary>(json);

        Assert.NotNull(deserialized);
        Assert.Equal(long.MaxValue, deserialized.DurationMs);
    }

    [Fact]
    public void HookOperationResult_WithDiff_RoundTrips()
    {
        var result = new HookOperationResult(true, "claude", "some-diff", "ok");
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<HookOperationResult>(json);

        Assert.NotNull(deserialized);
        Assert.True(deserialized.Success);
        Assert.Equal("claude", deserialized.Client);
        Assert.Equal("some-diff", deserialized.Diff);
        Assert.Equal("ok", deserialized.Message);
    }

    [Fact]
    public void HookOperationResult_NullDiff_RoundTrips()
    {
        var result = new HookOperationResult(false, "copilot", null, "failed");
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<HookOperationResult>(json);

        Assert.NotNull(deserialized);
        Assert.False(deserialized.Success);
        Assert.Equal("copilot", deserialized.Client);
        Assert.Null(deserialized.Diff);
        Assert.Equal("failed", deserialized.Message);
    }

    [Fact]
    public void HookStatusResult_AllTrue_RoundTrips()
    {
        var result = new HookStatusResult("claude", true, true, true, null);
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<HookStatusResult>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("claude", deserialized.Client);
        Assert.True(deserialized.SettingsFileExists);
        Assert.True(deserialized.HookInstalled);
        Assert.True(deserialized.EndpointReachable);
        Assert.Null(deserialized.EndpointWarning);
    }

    [Fact]
    public void HookStatusResult_WithWarning_RoundTrips()
    {
        var result = new HookStatusResult("copilot", true, true, false, "timeout");
        var json = JsonSerializer.Serialize(result);
        var deserialized = JsonSerializer.Deserialize<HookStatusResult>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("copilot", deserialized.Client);
        Assert.False(deserialized.EndpointReachable);
        Assert.Equal("timeout", deserialized.EndpointWarning);
    }

    [Fact]
    public void SyncStateEntry_RoundTrips()
    {
        var now = DateTimeOffset.UtcNow;
        var entry = new SyncStateEntry("/a/b.md", "abc123", now);
        var json = JsonSerializer.Serialize(entry);
        var deserialized = JsonSerializer.Deserialize<SyncStateEntry>(json);

        Assert.NotNull(deserialized);
        Assert.Equal("/a/b.md", deserialized.FilePath);
        Assert.Equal("abc123", deserialized.LastHash);
        Assert.Equal(entry.SyncedAtUtc.ToUnixTimeSeconds(), deserialized.SyncedAtUtc.ToUnixTimeSeconds());
    }
}
