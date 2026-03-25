using Xunit;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Services;

namespace NebulaRAG.Tests;

public class MemoryTierTests
{
    [Fact]
    public void MemoryTier_KnownValues_AreCorrect()
    {
        Assert.Equal("short_term", MemoryTier.ShortTerm);
        Assert.Equal("long_term", MemoryTier.LongTerm);
    }

    [Theory]
    [InlineData("short_term", true)]
    [InlineData("long_term", true)]
    [InlineData("medium", false)]
    [InlineData("LONG_TERM", false)]
    [InlineData("", false)]
    [InlineData(null, false)]
    public void MemoryTier_IsValid_CorrectlyClassifies(string? value, bool expected)
    {
        Assert.Equal(expected, MemoryTier.IsValid(value));
    }

    [Fact]
    public void AutoMemorySettings_RetentionDaysAlias_MapsToShortTerm()
    {
        var settings = new AutoMemorySettings { RetentionDays = 14 };
        Assert.Equal(14, settings.ResolvedShortTermRetentionDays);
    }

    [Fact]
    public void AutoMemorySettings_Defaults_AreCorrect()
    {
        var settings = new AutoMemorySettings();
        Assert.Equal(30, settings.ShortTermRetentionDays);
        Assert.Equal(90, settings.LongTermReviewIntervalDays);
    }

    [Fact]
    public void AutoMemorySettings_Validate_InvalidReviewInterval_AccumulatesError()
    {
        var settings = new AutoMemorySettings { LongTermReviewIntervalDays = 0 };
        var errors = new List<string>();
        settings.Validate(errors);
        Assert.Single(errors);
    }

    [Fact]
    public void AutoMemorySettings_Validate_NegativeShortTermRetention_AccumulatesError()
    {
        var settings = new AutoMemorySettings { ShortTermRetentionDays = -1 };
        var errors = new List<string>();
        settings.Validate(errors);
        Assert.Single(errors);
    }

    [Fact]
    public void AutoMemorySettings_Validate_BothFieldsInvalid_AccumulatesTwoErrors()
    {
        var settings = new AutoMemorySettings
        {
            ShortTermRetentionDays = -1,
            LongTermReviewIntervalDays = 0
        };
        var errors = new List<string>();
        settings.Validate(errors);
        Assert.Equal(2, errors.Count);
    }

    [Fact]
    public void MemoryRecord_HasTierAndReviewFields()
    {
        var record = new MemoryRecord(1, "s1", null, "episodic", "content",
            [], DateTimeOffset.UtcNow, MemoryTier.ShortTerm, null);
        Assert.Equal(MemoryTier.ShortTerm, record.Tier);
        Assert.Null(record.LastReviewedAtUtc);
    }

    [Fact]
    public void IAutoMemoryStore_HasDeleteByTierMethod()
    {
        var methods = typeof(IAutoMemoryStore).GetMethods();
        Assert.Contains(methods, m => m.Name == "DeleteMemoriesByTierOlderThanAsync");
    }

    [Fact]
    public void IAutoMemoryStore_HasListMemoriesDueForReviewMethod()
    {
        var methods = typeof(IAutoMemoryStore).GetMethods();
        Assert.Contains(methods, m => m.Name == "ListMemoriesDueForReviewAsync");
    }

    [Fact]
    public async Task AutoMemorySyncService_PrunePhase_UsesShortTermRetentionDays()
    {
        var store = new FakeAutoMemoryStore();
        var settings = new RagSettings
        {
            AutoMemory = new AutoMemorySettings { ShortTermRetentionDays = 7 }
        };
        var svc = new AutoMemorySyncService(store, new FakeAutoMemoryIndexer(), settings,
            Microsoft.Extensions.Logging.Abstractions.NullLogger<AutoMemorySyncService>.Instance);

        await svc.SyncAsync();

        Assert.True(store.DeleteByTierWasCalled);
        Assert.Equal(MemoryTier.ShortTerm, store.LastDeletedTier);
        Assert.False(store.DeleteByTagWasCalled);
    }
}

internal sealed class FakeAutoMemoryStore : IAutoMemoryStore
{
    public bool DeleteByTierWasCalled { get; private set; }
    public bool DeleteByTagWasCalled { get; private set; }
    public string? LastDeletedTier { get; private set; }

    public Task<int> DeleteMemoriesByTierOlderThanAsync(string tier, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        DeleteByTierWasCalled = true;
        LastDeletedTier = tier;
        return Task.FromResult(0);
    }

    public Task<int> DeleteMemoriesByTagOlderThanAsync(string tagPrefix, DateTimeOffset cutoff, CancellationToken ct = default)
    {
        DeleteByTagWasCalled = true;
        return Task.FromResult(0);
    }

    public Task<SyncStateEntry?> GetSyncStateAsync(string filePath, CancellationToken cancellationToken = default)
        => Task.FromResult<SyncStateEntry?>(null);

    public Task UpsertSyncStateAsync(string filePath, string hash, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task<IReadOnlyList<SyncStateEntry>> ListStaleSyncEntriesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SyncStateEntry>>(Array.Empty<SyncStateEntry>());

    public Task<IReadOnlyList<MemoryReviewResult>> ListMemoriesDueForReviewAsync(int reviewIntervalDays, int limit = 50, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<MemoryReviewResult>>(Array.Empty<MemoryReviewResult>());

    public Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(int limit = 100, CancellationToken cancellationToken = default)
        => Task.FromResult<IReadOnlyList<SourceInfo>>(Array.Empty<SourceInfo>());
}

internal sealed class FakeAutoMemoryIndexer : IAutoMemoryIndexer
{
    public Task IngestFileAsync(string filePath, string projectSlug, CancellationToken cancellationToken = default)
        => Task.CompletedTask;

    public Task ReindexSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
        => Task.CompletedTask;
}
