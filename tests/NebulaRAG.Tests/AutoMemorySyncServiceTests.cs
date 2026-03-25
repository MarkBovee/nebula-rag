using Microsoft.Extensions.Logging.Abstractions;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;
using NSubstitute;

namespace NebulaRAG.Tests;

public sealed class AutoMemorySyncServiceTests
{
    private static RagSettings MakeSettings(string baseDir, int retentionDays = 30) =>
        new() { AutoMemory = new AutoMemorySettings { BaseDirectory = baseDir, RetentionDays = retentionDays } };

    [Fact]
    public async Task Sync_NewFile_IsIngested()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var projectDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "my-project", "memory"));
        var mdFile = Path.Combine(projectDir.FullName, "MEMORY.md");
        await File.WriteAllTextAsync(mdFile, "# memory content");

        var store = Substitute.For<IAutoMemoryStore>();
        store.GetSyncStateAsync(mdFile, Arg.Any<CancellationToken>()).Returns((SyncStateEntry?)null);

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.FilesIngested);
        Assert.Empty(result.Errors);
        await indexer.Received(1).IngestFileAsync(mdFile, "my-project", Arg.Any<CancellationToken>());

        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_UnchangedFile_IsSkipped()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var projectDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "proj", "memory"));
        var mdFile = Path.Combine(projectDir.FullName, "MEMORY.md");
        var content = "# same content";
        await File.WriteAllTextAsync(mdFile, content);
        var hash = AutoMemorySyncService.ComputeHash(content);

        var store = Substitute.For<IAutoMemoryStore>();
        store.GetSyncStateAsync(mdFile, Arg.Any<CancellationToken>())
             .Returns(new SyncStateEntry(mdFile, hash, DateTimeOffset.UtcNow));

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(0, result.FilesIngested);
        await indexer.DidNotReceive().IngestFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>());
        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_IngestFailure_AddsErrorAndContinues()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var projectDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "proj", "memory"));
        var mdFile = Path.Combine(projectDir.FullName, "MEMORY.md");
        await File.WriteAllTextAsync(mdFile, "# good");

        var store = Substitute.For<IAutoMemoryStore>();
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncStateEntry?)null);

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        indexer.IngestFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<Task>(_ => throw new IOException("disk error"));

        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);
        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Single(result.Errors);
        Assert.Contains("disk error", result.Errors[0]);
        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_RetentionDaysZero_SkipsPruning()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var store = Substitute.For<IAutoMemoryStore>();
        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName, retentionDays: 0), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(0, result.MemoriesPruned);
        await store.DidNotReceive().DeleteMemoriesByTierOlderThanAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_StaleMemories_ArePruned()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var store = Substitute.For<IAutoMemoryStore>();
        store.DeleteMemoriesByTierOlderThanAsync(MemoryTier.ShortTerm, Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
             .Returns(3);

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName, retentionDays: 30), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(3, result.MemoriesPruned);
        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_DirtySource_IsReindexed()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var srcDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "src"));
        var srcFile = Path.Combine(srcDir.FullName, "file.md");
        await File.WriteAllTextAsync(srcFile, "new content");

        var store = Substitute.For<IAutoMemoryStore>();
        store.ListSourcesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new List<SourceInfo> { new SourceInfo(srcFile, null, 2, DateTime.UtcNow, "oldhash") });

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.SourcesReindexed);
        await indexer.Received(1).ReindexSourceAsync(srcFile, Arg.Any<CancellationToken>());
        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_MissingSourceFile_AddsWarning_DoesNotDeleteSource()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var store = Substitute.For<IAutoMemoryStore>();
        store.ListSourcesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new List<SourceInfo> { new SourceInfo("/nonexistent/path.md", null, 1, DateTime.UtcNow, "abc123") });

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(0, result.SourcesReindexed);
        Assert.Single(result.Errors);
        Assert.Contains("missing", result.Errors[0], StringComparison.OrdinalIgnoreCase);
        await indexer.DidNotReceive().ReindexSourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_CleanSource_IsNotReindexed()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var srcDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "src"));
        var srcFile = Path.Combine(srcDir.FullName, "file.md");
        var content = "# unchanged content";
        await File.WriteAllTextAsync(srcFile, content);
        var currentHash = AutoMemorySyncService.ComputeHash(content);

        var store = Substitute.For<IAutoMemoryStore>();
        store.ListSourcesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new List<SourceInfo> { new SourceInfo(srcFile, null, 3, DateTime.UtcNow, currentHash) });

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(0, result.SourcesReindexed);
        await indexer.DidNotReceive().ReindexSourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        dir.Delete(true);
    }
}
