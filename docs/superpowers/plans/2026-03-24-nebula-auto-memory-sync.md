# Nebula Auto-Memory Sync & Hook Setup — Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add `memory(action: "sync")` MCP tool that bridges Claude Code auto-memory files into Nebula RAG, prunes stale memories, and reindexes dirty sources — plus a `nebula_setup` MCP tool that installs/uninstalls/reports the Stop hook in client settings files.

**Architecture:** Two new Core services (`AutoMemorySyncService`, `HookInstallService`) backed by a new `nebula_sync_state` Postgres table. Both exposed as MCP tools — `sync` added to the existing `memory` tool switch, `nebula_setup` registered as a new top-level tool alongside `memory` and `rag_query`. The Claude Code `Stop` hook fires `memory(action: "sync")` automatically on session end.

**Tech Stack:** .NET 10, xUnit, Npgsql, PostgreSQL, `System.Text.Json`, `System.Security.Cryptography.SHA256`

---

## File Map

### New files
| File | Purpose |
|------|---------|
| `src/NebulaRAG.Core/Models/SyncModels.cs` | `SyncSummary`, `HookOperationResult`, `HookStatusResult` records |
| `src/NebulaRAG.Core/Services/AutoMemorySyncService.cs` | 3-phase sync: bridge, prune, reindex |
| `src/NebulaRAG.Core/Services/HookInstallService.cs` | Install/uninstall/status for claude + copilot hooks |
| `src/NebulaRAG.Mcp/Tools/NebulaSetupToolHandler.cs` | MCP handler for `nebula_setup` tool |
| `tests/NebulaRAG.Tests/AutoMemorySyncServiceTests.cs` | Unit tests for all 3 sync phases |
| `tests/NebulaRAG.Tests/HookInstallServiceTests.cs` | Unit tests for hook install/uninstall/status |

### Modified files
| File | Change |
|------|--------|
| `src/NebulaRAG.Core/Storage/PostgresRagStore.cs` | Add `EnsureSyncStateTableAsync`, `GetSyncStateAsync`, `UpsertSyncStateAsync`, `ListStaleSyncEntriesAsync`, `DeleteMemoriesByTagOlderThanAsync` |
| `src/NebulaRAG.Core/Configuration/RagSettings.cs` | Add `AutoMemorySettings` section |
| `src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs` | Add `"sync"` case to `ExecuteMemoryToolAsync` + update error message |
| `src/NebulaRAG.Core/Mcp/McpTransportHandler.cs` | Accept `AutoMemorySyncService` + `HookInstallService`; register `nebula_setup` in tools list; bump version to `0.3.83` |
| `src/NebulaRAG.Mcp/Program.cs` | Instantiate `AutoMemorySyncService` + `HookInstallService` + `NebulaSetupToolHandler`; wire into `McpTransportHandler` |
| `tests/NebulaRAG.Tests/McpTransportHandlerContractTests.cs` | Update version assertion to `0.3.83`; add `nebula_setup` tool list assertion |

---

## Task 1: Data models

**Files:**
- Create: `src/NebulaRAG.Core/Models/SyncModels.cs`

- [ ] **Step 1: Create the models file**

```csharp
namespace NebulaRAG.Core.Models;

/// <summary>Result of a memory(action: "sync") call.</summary>
public sealed record SyncSummary(
    int FilesIngested,
    int MemoriesPruned,
    int SourcesReindexed,
    IReadOnlyList<string> Errors,
    long DurationMs);

/// <summary>Result of a nebula_setup install-hooks or uninstall-hooks call.</summary>
public sealed record HookOperationResult(
    bool Success,
    string Client,
    string? Diff,
    string Message);

/// <summary>Per-client status returned by nebula_setup status.</summary>
public sealed record HookStatusResult(
    string Client,
    bool SettingsFileExists,
    bool HookInstalled,
    bool EndpointReachable,
    string? EndpointWarning);

/// <summary>One row from nebula_sync_state.</summary>
public sealed record SyncStateEntry(
    string FilePath,
    string LastHash,
    DateTimeOffset SyncedAt);
```

- [ ] **Step 2: Add `SyncSummary` serialization test**

Add to `tests/NebulaRAG.Tests/` a new file `SyncModelsTests.cs`:

```csharp
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
```

- [ ] **Step 3: Build to verify zero errors**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj -q
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/NebulaRAG.Core/Models/SyncModels.cs
git commit -m "feat: add SyncSummary, HookOperationResult, HookStatusResult, SyncStateEntry models"
```

---

## Task 2: AutoMemorySettings config section

**Files:**
- Modify: `src/NebulaRAG.Core/Configuration/RagSettings.cs`

- [ ] **Step 1: Add `AutoMemorySettings` class and wire it into `RagSettings`**

Add at the bottom of `RagSettings.cs` (before the closing of the file):

```csharp
/// <summary>
/// Settings for Claude Code auto-memory sync integration.
/// </summary>
public sealed class AutoMemorySettings
{
    /// <summary>
    /// Base directory for Claude Code auto-memory project files.
    /// Tilde (~) is resolved via Environment.GetFolderPath(SpecialFolder.UserProfile).
    /// Default: ~/.claude/projects
    /// </summary>
    public string BaseDirectory { get; init; } = "~/.claude/projects";

    /// <summary>
    /// Number of days after last sync before an auto-memory entry is pruned.
    /// Set to 0 to disable pruning entirely.
    /// Default: 30
    /// </summary>
    public int RetentionDays { get; init; } = 30;
}
```

Add the property to `RagSettings`:
```csharp
public AutoMemorySettings AutoMemory { get; init; } = new();
```

- [ ] **Step 2: Build**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj -q
```
Expected: `Build succeeded.`

- [ ] **Step 3: Commit**

```bash
git add src/NebulaRAG.Core/Configuration/RagSettings.cs
git commit -m "feat: add AutoMemorySettings config section with BaseDirectory and RetentionDays"
```

---

## Task 3: PostgresRagStore — sync state + memory prune methods

**Files:**
- Modify: `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`

The `nebula_sync_state` table is created as part of the existing `EnsureSchemaAsync` call. `DeleteMemoriesByTagOlderThanAsync` prunes stale memories by tag prefix + age.

- [ ] **Step 1: Add `nebula_sync_state` DDL to `EnsureSchemaAsync`**

Find the end of the `sql` string literal in `EnsureSchemaAsync` (just before the closing `"""`). Add:

```sql
CREATE TABLE IF NOT EXISTS nebula_sync_state (
    file_path   TEXT        PRIMARY KEY,
    last_hash   TEXT        NOT NULL,
    synced_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

- [ ] **Step 2: Add sync state CRUD methods**

Add these methods to `PostgresRagStore` (after the memory methods):

```csharp
/// <summary>Gets the sync state entry for a file path, or null if not tracked.</summary>
public async Task<SyncStateEntry?> GetSyncStateAsync(string filePath, CancellationToken cancellationToken = default)
{
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var cmd = new NpgsqlCommand(
        "SELECT file_path, last_hash, synced_at FROM nebula_sync_state WHERE file_path = @fp",
        connection);
    cmd.Parameters.AddWithValue("fp", filePath);
    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
    if (!await reader.ReadAsync(cancellationToken)) return null;
    return new SyncStateEntry(
        reader.GetString(0),
        reader.GetString(1),
        reader.GetFieldValue<DateTimeOffset>(2));
}

/// <summary>Inserts or updates the sync state for a file.</summary>
public async Task UpsertSyncStateAsync(string filePath, string hash, CancellationToken cancellationToken = default)
{
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var cmd = new NpgsqlCommand("""
        INSERT INTO nebula_sync_state (file_path, last_hash, synced_at)
        VALUES (@fp, @hash, NOW())
        ON CONFLICT (file_path) DO UPDATE
            SET last_hash = EXCLUDED.last_hash, synced_at = NOW()
        """, connection);
    cmd.Parameters.AddWithValue("fp", filePath);
    cmd.Parameters.AddWithValue("hash", hash);
    await cmd.ExecuteNonQueryAsync(cancellationToken);
}

/// <summary>
/// Lists nebula_sync_state entries not updated since the cutoff (used for pruning).
/// </summary>
public async Task<IReadOnlyList<SyncStateEntry>> ListStaleSyncEntriesAsync(
    DateTimeOffset cutoff, CancellationToken cancellationToken = default)
{
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var cmd = new NpgsqlCommand(
        "SELECT file_path, last_hash, synced_at FROM nebula_sync_state WHERE synced_at < @cutoff",
        connection);
    cmd.Parameters.AddWithValue("cutoff", cutoff);
    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
    var results = new List<SyncStateEntry>();
    while (await reader.ReadAsync(cancellationToken))
        results.Add(new SyncStateEntry(reader.GetString(0), reader.GetString(1), reader.GetFieldValue<DateTimeOffset>(2)));
    return results;
}

/// <summary>
/// Deletes all memory entries tagged with the given prefix that are older than the cutoff.
/// Uses created_at for simplicity — entries are re-created fresh each sync cycle.
/// Returns count of deleted entries.
/// </summary>
public async Task<int> DeleteMemoriesByTagOlderThanAsync(
    string tagPrefix, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
{
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var cmd = new NpgsqlCommand("""
        DELETE FROM memories
        WHERE EXISTS (SELECT 1 FROM unnest(tags) t WHERE t LIKE @tagPattern)
        AND created_at < @cutoff
        """, connection);
    cmd.Parameters.AddWithValue("tagPattern", tagPrefix + "%");
    cmd.Parameters.AddWithValue("cutoff", cutoff);
    return await cmd.ExecuteNonQueryAsync(cancellationToken);
}
```

- [ ] **Step 3: Build**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj -q
```
Expected: `Build succeeded.`

- [ ] **Step 4: Commit**

```bash
git add src/NebulaRAG.Core/Storage/PostgresRagStore.cs
git commit -m "feat: add nebula_sync_state DDL and sync state CRUD + memory prune methods to PostgresRagStore"
```

---

## Task 4: AutoMemorySyncService

**Files:**
- Create: `src/NebulaRAG.Core/Services/AutoMemorySyncService.cs`

- [ ] **Step 1: Write the failing tests first**

Create `tests/NebulaRAG.Tests/AutoMemorySyncServiceTests.cs`:

```csharp
using Microsoft.Extensions.Logging.Abstractions;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;
using NSubstitute;

namespace NebulaRAG.Tests;

/// <summary>
/// Unit tests for AutoMemorySyncService using a fake file system and mocked store.
/// </summary>
public sealed class AutoMemorySyncServiceTests
{
    // --- helpers ---

    private static RagSettings MakeSettings(string baseDir, int retentionDays = 30) =>
        new() { AutoMemory = new AutoMemorySettings { BaseDirectory = baseDir, RetentionDays = retentionDays } };

    // --- Phase 1: Bridge ---

    [Fact]
    public async Task Sync_NewFile_IsIngested()
    {
        // Arrange: temp dir with one .md file
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var projectDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "my-project", "memory"));
        var mdFile = Path.Combine(projectDir.FullName, "MEMORY.md");
        await File.WriteAllTextAsync(mdFile, "# memory content");

        var store = Substitute.For<IAutoMemoryStore>();
        store.GetSyncStateAsync(mdFile, Arg.Any<CancellationToken>()).Returns((SyncStateEntry?)null);

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var settings = MakeSettings(dir.FullName);
        var svc = new AutoMemorySyncService(store, indexer, settings, NullLogger<AutoMemorySyncService>.Instance);

        // Act
        var result = await svc.SyncAsync(CancellationToken.None);

        // Assert
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
    public async Task Sync_UnreadableFile_AddsErrorAndContinues()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var projectDir = Directory.CreateDirectory(Path.Combine(dir.FullName, "proj", "memory"));
        // Create a valid file alongside a locked/unreadable path by using a non-existent path trick
        var goodFile = Path.Combine(projectDir.FullName, "MEMORY.md");
        await File.WriteAllTextAsync(goodFile, "# good");

        var store = Substitute.For<IAutoMemoryStore>();
        store.GetSyncStateAsync(Arg.Any<string>(), Arg.Any<CancellationToken>()).Returns((SyncStateEntry?)null);

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        // Simulate ingest failure on the good file
        indexer.IngestFileAsync(Arg.Any<string>(), Arg.Any<string>(), Arg.Any<CancellationToken>())
               .Returns<Task>(x => throw new IOException("disk error"));

        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);
        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.Errors.Count);
        Assert.Contains("disk error", result.Errors[0]);
        dir.Delete(true);
    }

    // --- Phase 2: Prune ---

    [Fact]
    public async Task Sync_RetentionDaysZero_SkipsPruning()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var store = Substitute.For<IAutoMemoryStore>();
        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName, retentionDays: 0), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(0, result.MemoriesPruned);
        await store.DidNotReceive().DeleteMemoriesByTagOlderThanAsync(Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_StaleMemories_ArePruned()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var store = Substitute.For<IAutoMemoryStore>();
        store.DeleteMemoriesByTagOlderThanAsync("auto-memory", Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>())
             .Returns(3);

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName, retentionDays: 30), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(3, result.MemoriesPruned);
        dir.Delete(true);
    }

    // --- Phase 3: Reindex ---

    [Fact]
    public async Task Sync_DirtySource_IsReindexed()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var store = Substitute.For<IAutoMemoryStore>();
        store.ListSourcesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new List<SourceInfo> { new SourceInfo("src/file.md", 2, DateTimeOffset.UtcNow, "oldhash") });

        // Write a real file with different content
        Directory.CreateDirectory(Path.Combine(dir.FullName, "src"));
        await File.WriteAllTextAsync(Path.Combine(dir.FullName, "src/file.md"), "new content");

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(1, result.SourcesReindexed);
        await indexer.Received(1).ReindexSourceAsync("src/file.md", Arg.Any<CancellationToken>());
        dir.Delete(true);
    }

    [Fact]
    public async Task Sync_MissingSourceFile_AddsWarningToErrors_DoesNotDeleteSource()
    {
        var dir = Directory.CreateTempSubdirectory("nebula-test-");
        var store = Substitute.For<IAutoMemoryStore>();
        store.ListSourcesAsync(Arg.Any<int>(), Arg.Any<CancellationToken>())
             .Returns(new List<SourceInfo> { new SourceInfo("/nonexistent/path.md", 1, DateTimeOffset.UtcNow, "abc123") });

        var indexer = Substitute.For<IAutoMemoryIndexer>();
        var svc = new AutoMemorySyncService(store, indexer, MakeSettings(dir.FullName), NullLogger<AutoMemorySyncService>.Instance);

        var result = await svc.SyncAsync(CancellationToken.None);

        Assert.Equal(0, result.SourcesReindexed);
        Assert.Single(result.Errors); // warning counted as error entry
        Assert.Contains("missing", result.Errors[0], StringComparison.OrdinalIgnoreCase);
        // Source must NOT be deleted from the store
        await store.DidNotReceive().DeleteMemoriesByTagOlderThanAsync(
            Arg.Any<string>(), Arg.Any<DateTimeOffset>(), Arg.Any<CancellationToken>());
        await indexer.DidNotReceive().ReindexSourceAsync(Arg.Any<string>(), Arg.Any<CancellationToken>());
        dir.Delete(true);
    }
}
```

- [ ] **Step 2: Check and add NSubstitute to test project**

First verify if NSubstitute is already referenced:
```bash
grep -i "NSubstitute" tests/NebulaRAG.Tests/NebulaRAG.Tests.csproj
```
If the grep returns nothing, add it:
```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet add tests/NebulaRAG.Tests/NebulaRAG.Tests.csproj package NSubstitute
```

- [ ] **Step 3: Verify `ListSourcesAsync` signature before writing interface**

```bash
grep -n "ListSourcesAsync" src/NebulaRAG.Core/Storage/PostgresRagStore.cs | head -5
```
Confirm the method exists and note its exact parameter list. The interface in Step 4 uses `(int limit = 1000, CancellationToken ct)` — adjust if the real signature differs.

Also verify the delete-by-source-path method name:
```bash
grep -n "DeleteDocument.*SourcePath\|DeleteDocumentBySource" src/NebulaRAG.Core/Storage/PostgresRagStore.cs | head -5
```
Note the exact method name (singular or plural) — used in Task 4 Step 6.

- [ ] **Step 4: Define interfaces `IAutoMemoryStore` and `IAutoMemoryIndexer`**

Create `src/NebulaRAG.Core/Services/IAutoMemoryStore.cs`:

```csharp
using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>Storage abstraction used by AutoMemorySyncService (enables unit testing).</summary>
public interface IAutoMemoryStore
{
    Task<SyncStateEntry?> GetSyncStateAsync(string filePath, CancellationToken cancellationToken = default);
    Task UpsertSyncStateAsync(string filePath, string hash, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SyncStateEntry>> ListStaleSyncEntriesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    Task<int> DeleteMemoriesByTagOlderThanAsync(string tagPrefix, DateTimeOffset cutoff, CancellationToken cancellationToken = default);
    Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(int limit = 1000, CancellationToken cancellationToken = default);
}
```

Create `src/NebulaRAG.Core/Services/IAutoMemoryIndexer.cs`:

```csharp
namespace NebulaRAG.Core.Services;

/// <summary>Indexing abstraction used by AutoMemorySyncService (enables unit testing).</summary>
public interface IAutoMemoryIndexer
{
    Task IngestFileAsync(string filePath, string projectSlug, CancellationToken cancellationToken = default);
    Task ReindexSourceAsync(string sourcePath, CancellationToken cancellationToken = default);
}
```

Make `PostgresRagStore` implement `IAutoMemoryStore` (add `: IAutoMemoryStore` to its class declaration — all required methods already exist or were added in Task 3).

- [ ] **Step 4: Create `AutoMemorySyncService`**

Create `src/NebulaRAG.Core/Services/AutoMemorySyncService.cs`:

```csharp
using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Implements three-phase sync: auto-memory bridge, stale memory pruning, dirty RAG reindex.
/// </summary>
public sealed class AutoMemorySyncService
{
    private readonly IAutoMemoryStore _store;
    private readonly IAutoMemoryIndexer _indexer;
    private readonly RagSettings _settings;
    private readonly ILogger<AutoMemorySyncService> _logger;

    public AutoMemorySyncService(
        IAutoMemoryStore store,
        IAutoMemoryIndexer indexer,
        RagSettings settings,
        ILogger<AutoMemorySyncService> logger)
    {
        _store = store;
        _indexer = indexer;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Runs all three sync phases and returns a summary.</summary>
    public async Task<SyncSummary> SyncAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        int filesIngested = 0, memoriesPruned = 0, sourcesReindexed = 0;

        // Phase 1: Auto-Memory Bridge
        try { filesIngested = await BridgeAutoMemoryAsync(errors, cancellationToken); }
        catch (Exception ex) { errors.Add($"Phase1 fatal: {ex.Message}"); }

        // Phase 2: Stale Memory Pruning
        try { memoriesPruned = await PruneStaleMemoriesAsync(cancellationToken); }
        catch (Exception ex) { errors.Add($"Phase2 fatal: {ex.Message}"); }

        // Phase 3: Dirty RAG Source Reindex
        try { sourcesReindexed = await ReindexDirtySourcesAsync(errors, cancellationToken); }
        catch (Exception ex) { errors.Add($"Phase3 fatal: {ex.Message}"); }

        sw.Stop();
        return new SyncSummary(filesIngested, memoriesPruned, sourcesReindexed, errors, sw.ElapsedMilliseconds);
    }

    // --- Phase 1 ---

    private async Task<int> BridgeAutoMemoryAsync(List<string> errors, CancellationToken ct)
    {
        var baseDir = ResolveBaseDirectory(_settings.AutoMemory.BaseDirectory);
        if (!Directory.Exists(baseDir))
        {
            _logger.LogWarning("Auto-memory base directory not found: {Dir}. Phase 1 skipped.", baseDir);
            return 0;
        }

        var files = Directory.EnumerateFiles(baseDir, "*.md", SearchOption.AllDirectories)
                             .Where(f => f.Contains(Path.DirectorySeparatorChar + "memory" + Path.DirectorySeparatorChar))
                             .ToList();

        int ingested = 0;
        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var hash = ComputeHash(content);
                var existing = await _store.GetSyncStateAsync(file, ct);
                if (existing?.LastHash == hash) continue; // unchanged

                var slug = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file))) ?? "unknown";
                await _indexer.IngestFileAsync(file, slug, ct);
                await _store.UpsertSyncStateAsync(file, hash, ct);
                ingested++;
                _logger.LogInformation("Ingested auto-memory file: {File}", file);
            }
            catch (Exception ex)
            {
                errors.Add($"File {file}: {ex.Message}");
                _logger.LogWarning(ex, "Failed to ingest auto-memory file: {File}", file);
            }
        }
        return ingested;
    }

    // --- Phase 2 ---

    private async Task<int> PruneStaleMemoriesAsync(CancellationToken ct)
    {
        if (_settings.AutoMemory.RetentionDays == 0) return 0;
        var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.AutoMemory.RetentionDays);
        var pruned = await _store.DeleteMemoriesByTagOlderThanAsync("auto-memory", cutoff, ct);
        if (pruned > 0) _logger.LogInformation("Pruned {Count} stale auto-memory entries.", pruned);
        return pruned;
    }

    // --- Phase 3 ---

    private async Task<int> ReindexDirtySourcesAsync(List<string> errors, CancellationToken ct)
    {
        var sources = await _store.ListSourcesAsync(1000, ct);
        int reindexed = 0;
        foreach (var source in sources)
        {
            // Only process file-backed sources (not URLs)
            if (source.SourcePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.SourcePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!File.Exists(source.SourcePath))
            {
                errors.Add($"Source file missing (not deleted): {source.SourcePath}");
                _logger.LogWarning("RAG source file missing on disk: {Path}", source.SourcePath);
                continue;
            }

            var content = await File.ReadAllTextAsync(source.SourcePath, ct);
            var currentHash = ComputeHash(content);
            if (currentHash == source.ContentHash) continue; // clean

            await _indexer.ReindexSourceAsync(source.SourcePath, ct);
            reindexed++;
            _logger.LogInformation("Reindexed dirty source: {Path}", source.SourcePath);
        }
        return reindexed;
    }

    // --- Utilities ---

    /// <summary>Computes a SHA-256 hex hash of the given string content.</summary>
    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ResolveBaseDirectory(string path)
    {
        if (path.StartsWith("~/") || path == "~")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = home + path[1..];
        }
        return path;
    }
}
```

- [ ] **Step 5: Run the failing tests**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet test tests/NebulaRAG.Tests/ --filter "AutoMemorySyncServiceTests" -v minimal
```
Expected: Some tests fail (interfaces not yet wired to `PostgresRagStore`).

- [ ] **Step 6: Implement `IAutoMemoryIndexer` on `RagIndexer`**

Add `: IAutoMemoryIndexer` to `RagIndexer` class declaration.

Add the two interface methods to `RagIndexer`:

```csharp
/// <inheritdoc/>
public async Task IngestFileAsync(string filePath, string projectSlug, CancellationToken cancellationToken = default)
{
    // Delete existing chunks for this source first
    await _store.DeleteDocumentBySourcePathAsync($"auto-memory:{projectSlug}", cancellationToken);
    await IndexSingleFileAsync(filePath, $"auto-memory:{projectSlug}", cancellationToken);
}

/// <inheritdoc/>
public async Task ReindexSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
{
    await _store.DeleteDocumentBySourcePathAsync(sourcePath, cancellationToken);
    await IndexSingleFileAsync(sourcePath, null, cancellationToken);
}
```

> **Note:** `IndexSingleFileAsync` is the per-file indexing logic extracted from `IndexDirectoryAsync`. If it doesn't exist yet as a separate method, extract the file-processing loop body from `IndexDirectoryAsync` into a private `IndexSingleFileAsync(string filePath, string? projectId, CancellationToken ct)` helper.

Also add `DeleteDocumentBySourcePathAsync` to `PostgresRagStore` if not present (check — it may already exist as `DeleteDocumentsBySourcePathAsync`):

```csharp
public async Task<int> DeleteDocumentBySourcePathAsync(string sourcePath, CancellationToken cancellationToken = default)
{
    await using var connection = new NpgsqlConnection(_connectionString);
    await connection.OpenAsync(cancellationToken);
    await using var cmd = new NpgsqlCommand(
        "DELETE FROM rag_documents WHERE source_path = @sp", connection);
    cmd.Parameters.AddWithValue("sp", sourcePath);
    return await cmd.ExecuteNonQueryAsync(cancellationToken);
}
```

- [ ] **Step 7: Run all AutoMemorySyncService tests — expect pass**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet test tests/NebulaRAG.Tests/ --filter "AutoMemorySyncServiceTests" -v minimal
```
Expected: All tests pass.

- [ ] **Step 8: Run full test suite**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet test tests/NebulaRAG.Tests/ -v minimal
```
Expected: All existing tests still pass.

- [ ] **Step 9: Commit**

```bash
git add src/NebulaRAG.Core/Services/AutoMemorySyncService.cs \
        src/NebulaRAG.Core/Services/IAutoMemoryStore.cs \
        src/NebulaRAG.Core/Services/IAutoMemoryIndexer.cs \
        src/NebulaRAG.Core/Storage/PostgresRagStore.cs \
        src/NebulaRAG.Core/Services/RagIndexer.cs \
        tests/NebulaRAG.Tests/AutoMemorySyncServiceTests.cs
git commit -m "feat: implement AutoMemorySyncService with 3-phase sync (bridge, prune, reindex)"
```

---

## Task 5: HookInstallService

**Files:**
- Create: `src/NebulaRAG.Core/Services/HookInstallService.cs`
- Create: `tests/NebulaRAG.Tests/HookInstallServiceTests.cs`

- [ ] **Step 1: Write failing tests**

Create `tests/NebulaRAG.Tests/HookInstallServiceTests.cs`:

```csharp
using NebulaRAG.Core.Services;

namespace NebulaRAG.Tests;

/// <summary>
/// Unit tests for HookInstallService using temp files as settings.
/// </summary>
public sealed class HookInstallServiceTests
{
    private static readonly string NebulaHookCommand = "nebula-rag memory sync";

    private static string WriteSettings(string json)
    {
        var path = Path.GetTempFileName();
        File.WriteAllText(path, json);
        return path;
    }

    [Fact]
    public async Task InstallHooks_WritesHookEntry_IntoEmptySettings()
    {
        var path = WriteSettings("{}");
        var svc = new HookInstallService();
        var result = await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);

        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(path);
        Assert.Contains(NebulaHookCommand, content);
        File.Delete(path);
    }

    [Fact]
    public async Task InstallHooks_IsIdempotent()
    {
        var path = WriteSettings("{}");
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);
        await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path); // second call

        var content = await File.ReadAllTextAsync(path);
        var count = CountOccurrences(content, NebulaHookCommand);
        Assert.Equal(1, count);
        File.Delete(path);
    }

    [Fact]
    public async Task InstallHooks_DryRun_DoesNotWriteFile()
    {
        var path = WriteSettings("{}");
        var original = await File.ReadAllTextAsync(path);
        var svc = new HookInstallService();
        var result = await svc.InstallHooksAsync("claude", dryRun: true, settingsPathOverride: path);

        Assert.True(result.Success);
        Assert.NotNull(result.Diff);
        Assert.Contains(NebulaHookCommand, result.Diff);
        Assert.Equal(original, await File.ReadAllTextAsync(path)); // file unchanged
        File.Delete(path);
    }

    [Fact]
    public async Task UninstallHooks_RemovesHookEntry()
    {
        var path = WriteSettings("{}");
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);
        var result = await svc.UninstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);

        Assert.True(result.Success);
        var content = await File.ReadAllTextAsync(path);
        Assert.DoesNotContain(NebulaHookCommand, content);
        File.Delete(path);
    }

    [Fact]
    public async Task UninstallHooks_DryRun_DoesNotWriteFile()
    {
        var path = WriteSettings("{}");
        var svc = new HookInstallService();
        await svc.InstallHooksAsync("claude", dryRun: false, settingsPathOverride: path);
        var beforeUninstall = await File.ReadAllTextAsync(path);
        var result = await svc.UninstallHooksAsync("claude", dryRun: true, settingsPathOverride: path);

        Assert.True(result.Success);
        Assert.Equal(beforeUninstall, await File.ReadAllTextAsync(path));
        File.Delete(path);
    }

    private static int CountOccurrences(string text, string pattern) =>
        (text.Length - text.Replace(pattern, "").Length) / pattern.Length;
}
```

- [ ] **Step 2: Run tests — expect fail (class doesn't exist)**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet test tests/NebulaRAG.Tests/ --filter "HookInstallServiceTests" -v minimal 2>&1 | tail -5
```
Expected: Build error — `HookInstallService` not found.

- [ ] **Step 3: Implement HookInstallService**

Create `src/NebulaRAG.Core/Services/HookInstallService.cs`:

```csharp
using System.Runtime.InteropServices;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Installs, uninstalls, and reports the Nebula MCP Stop hook in supported AI client settings files.
/// </summary>
public sealed class HookInstallService
{
    private const string HookCommand = "nebula-rag memory sync";
    private const string HookMarker = "nebula-rag"; // used to detect existing entry

    private readonly ILogger<HookInstallService> _logger;

    public HookInstallService(ILogger<HookInstallService>? logger = null)
    {
        _logger = logger ?? Microsoft.Extensions.Logging.Abstractions.NullLogger<HookInstallService>.Instance;
    }

    /// <summary>
    /// Installs the Nebula Stop hook into the target client's settings file.
    /// </summary>
    public async Task<HookOperationResult> InstallHooksAsync(
        string client, bool dryRun = false, string? settingsPathOverride = null)
    {
        var path = settingsPathOverride ?? ResolveSettingsPath(client);
        if (path is null)
            return new HookOperationResult(false, client, null, $"Unsupported client: {client}");

        var json = File.Exists(path) ? await File.ReadAllTextAsync(path) : "{}";
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

        if (IsHookPresent(root))
            return new HookOperationResult(true, client, null, "Hook already installed (no change).");

        InjectHook(root);
        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var diff = $"+ Added Stop hook: {HookCommand}";

        if (dryRun)
            return new HookOperationResult(true, client, diff, "Dry run — no file written.");

        await AtomicWriteAsync(path, output);
        _logger.LogInformation("Installed Nebula Stop hook in {Path}", path);
        return new HookOperationResult(true, client, diff, $"Hook installed in {path}");
    }

    /// <summary>
    /// Removes the Nebula Stop hook from the target client's settings file.
    /// </summary>
    public async Task<HookOperationResult> UninstallHooksAsync(
        string client, bool dryRun = false, string? settingsPathOverride = null)
    {
        var path = settingsPathOverride ?? ResolveSettingsPath(client);
        if (path is null)
            return new HookOperationResult(false, client, null, $"Unsupported client: {client}");

        if (!File.Exists(path))
            return new HookOperationResult(true, client, null, "Settings file not found — nothing to remove.");

        var json = await File.ReadAllTextAsync(path);
        var root = JsonNode.Parse(json)?.AsObject() ?? new JsonObject();

        if (!IsHookPresent(root))
            return new HookOperationResult(true, client, null, "Hook not present — nothing to remove.");

        RemoveHook(root);
        var output = root.ToJsonString(new JsonSerializerOptions { WriteIndented = true });
        var diff = $"- Removed Stop hook: {HookCommand}";

        if (dryRun)
            return new HookOperationResult(true, client, diff, "Dry run — no file written.");

        await AtomicWriteAsync(path, output);
        _logger.LogInformation("Removed Nebula Stop hook from {Path}", path);
        return new HookOperationResult(true, client, diff, $"Hook removed from {path}");
    }

    /// <summary>
    /// Returns status for all supported clients.
    /// </summary>
    public async Task<IReadOnlyList<HookStatusResult>> GetStatusAsync(
        string? nebulaEndpoint = null, CancellationToken cancellationToken = default)
    {
        var clients = new[] { "claude", "copilot" };
        var results = new List<HookStatusResult>();

        foreach (var client in clients)
        {
            var path = ResolveSettingsPath(client);
            var exists = path is not null && File.Exists(path);
            bool hookInstalled = false;

            if (exists)
            {
                var json = await File.ReadAllTextAsync(path!, cancellationToken);
                var root = JsonNode.Parse(json)?.AsObject();
                hookInstalled = root is not null && IsHookPresent(root);
            }

            (bool reachable, string? warning) = await CheckEndpointAsync(
                nebulaEndpoint ?? "http://localhost:5001/health", cancellationToken);

            results.Add(new HookStatusResult(client, exists, hookInstalled, reachable, warning));
        }

        return results;
    }

    // --- Internals ---

    private static bool IsHookPresent(JsonObject root)
    {
        var hooksNode = root["hooks"]?.AsObject();
        if (hooksNode is null) return false;
        var stopArr = hooksNode["Stop"]?.AsArray();
        if (stopArr is null) return false;
        return stopArr.Any(entry =>
            entry?["hooks"]?.AsArray()
                   ?.Any(h => h?["command"]?.GetValue<string>()?.Contains(HookMarker) == true) == true);
    }

    private static void InjectHook(JsonObject root)
    {
        var hooks = root["hooks"]?.AsObject() ?? new JsonObject();
        var stopArr = hooks["Stop"]?.AsArray() ?? new JsonArray();

        var hookEntry = new JsonObject
        {
            ["matcher"] = "",
            ["hooks"] = new JsonArray
            {
                new JsonObject { ["type"] = "command", ["command"] = HookCommand }
            }
        };
        stopArr.Add(hookEntry);
        hooks["Stop"] = stopArr;
        root["hooks"] = hooks;
    }

    private static void RemoveHook(JsonObject root)
    {
        var stopArr = root["hooks"]?["Stop"]?.AsArray();
        if (stopArr is null) return;
        var toRemove = stopArr
            .Where(entry =>
                entry?["hooks"]?.AsArray()
                    ?.Any(h => h?["command"]?.GetValue<string>()?.Contains(HookMarker) == true) == true)
            .ToList();
        foreach (var item in toRemove) stopArr.Remove(item);
    }

    private static async Task AtomicWriteAsync(string path, string content)
    {
        var dir = Path.GetDirectoryName(path);
        if (!string.IsNullOrWhiteSpace(dir)) Directory.CreateDirectory(dir);
        var tmp = path + ".nebula.tmp";
        await File.WriteAllTextAsync(tmp, content);
        File.Move(tmp, path, overwrite: true);
    }

    private static async Task<(bool reachable, string? warning)> CheckEndpointAsync(
        string url, CancellationToken cancellationToken)
    {
        try
        {
            using var http = new HttpClient { Timeout = TimeSpan.FromSeconds(2) };
            var resp = await http.GetAsync(url, cancellationToken);
            return (resp.IsSuccessStatusCode, null);
        }
        catch (Exception ex)
        {
            return (false, $"Endpoint unreachable: {ex.Message}");
        }
    }

    internal static string? ResolveSettingsPath(string client) => client.ToLowerInvariant() switch
    {
        "claude" => Path.Combine(
            Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
            ".claude", "settings.json"),
        "copilot" => RuntimeInformation.IsOSPlatform(OSPlatform.Windows)
            ? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData),
                "GitHub Copilot", "settings.json")
            : Path.Combine(
                Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                ".config", "github-copilot", "settings.json"),
        _ => null
    };
}
```

- [ ] **Step 4: Run tests — expect pass**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet test tests/NebulaRAG.Tests/ --filter "HookInstallServiceTests" -v minimal
```
Expected: All 5 tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/NebulaRAG.Core/Services/HookInstallService.cs \
        tests/NebulaRAG.Tests/HookInstallServiceTests.cs
git commit -m "feat: implement HookInstallService with install/uninstall/status + dry-run support"
```

---

## Task 6: MCP handler wiring — memory sync action

**Files:**
- Modify: `src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs`
- Modify: `src/NebulaRAG.Core/Mcp/McpTransportHandler.cs`

- [ ] **Step 1: Add `AutoMemorySyncService` field and constructor param to `McpTransportHandler`**

In `McpTransportHandler.cs`, add `AutoMemorySyncService` as a constructor parameter and store it as a `readonly` field:

```csharp
private readonly AutoMemorySyncService _autoMemorySyncService;
```

Add to the constructor parameter list and assignment.

- [ ] **Step 2: Add `"sync"` case to `ExecuteMemoryToolAsync` in `McpTransportHandler.Tools.cs`**

Find the memory tool switch (around line 165). Add the `sync` case and update the error message:

```csharp
"sync" => await ExecuteMemorySyncToolAsync(cancellationToken),
```

Update the error string to include `sync`:
```csharp
return BuildToolResult("action is required and must be one of: store, recall, list, update, delete, sync.", isError: true);
```

Add the implementation method:

```csharp
/// <summary>Executes the three-phase auto-memory sync.</summary>
private async Task<JsonObject> ExecuteMemorySyncToolAsync(CancellationToken cancellationToken)
{
    var summary = await _autoMemorySyncService.SyncAsync(cancellationToken);
    return BuildToolResult("Sync complete.", new JsonObject
    {
        ["filesIngested"] = summary.FilesIngested,
        ["memoriesPruned"] = summary.MemoriesPruned,
        ["sourcesReindexed"] = summary.SourcesReindexed,
        ["errors"] = new JsonArray(summary.Errors.Select(e => JsonValue.Create(e)).ToArray()),
        ["durationMs"] = summary.DurationMs
    });
}
```

- [ ] **Step 4: Commit**

```bash
git add src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs \
        src/NebulaRAG.Core/Mcp/McpTransportHandler.cs
git commit -m "feat: add memory(action: sync) MCP handler wired to AutoMemorySyncService"

---

## Task 7: nebula_setup MCP tool

**Files:**
- Create: `src/NebulaRAG.Mcp/Tools/NebulaSetupToolHandler.cs`
- Modify: `src/NebulaRAG.Core/Mcp/McpTransportHandler.cs`
- Modify: `src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs`

- [ ] **Step 1: Create the tool handler**

Create `src/NebulaRAG.Mcp/Tools/NebulaSetupToolHandler.cs`:

```csharp
using System.Text.Json.Nodes;
using NebulaRAG.Core.Services;

namespace NebulaRAG.Mcp.Tools;

/// <summary>
/// Handles the nebula_setup MCP tool: install-hooks, uninstall-hooks, status.
/// </summary>
public sealed class NebulaSetupToolHandler
{
    private readonly HookInstallService _hookInstallService;

    public NebulaSetupToolHandler(HookInstallService hookInstallService)
    {
        _hookInstallService = hookInstallService;
    }

    public async Task<JsonObject> HandleAsync(JsonObject? arguments, CancellationToken cancellationToken)
    {
        var action = arguments?["action"]?.GetValue<string>();
        if (string.IsNullOrWhiteSpace(action))
            return BuildError("action is required and must be one of: install-hooks, uninstall-hooks, status.");

        return action switch
        {
            "install-hooks" => await HandleInstallAsync(arguments!, dryRun: GetDryRun(arguments), cancellationToken),
            "uninstall-hooks" => await HandleUninstallAsync(arguments!, dryRun: GetDryRun(arguments), cancellationToken),
            "status" => await HandleStatusAsync(arguments, cancellationToken),
            _ => BuildError("Unsupported action. Use: install-hooks, uninstall-hooks, status.")
        };
    }

    private async Task<JsonObject> HandleInstallAsync(JsonObject args, bool dryRun, CancellationToken ct)
    {
        var client = args["client"]?.GetValue<string>() ?? "claude";
        var result = await _hookInstallService.InstallHooksAsync(client, dryRun);
        return BuildResult(result.Success, result.Message, result.Diff);
    }

    private async Task<JsonObject> HandleUninstallAsync(JsonObject args, bool dryRun, CancellationToken ct)
    {
        var client = args["client"]?.GetValue<string>() ?? "claude";
        var result = await _hookInstallService.UninstallHooksAsync(client, dryRun);
        return BuildResult(result.Success, result.Message, result.Diff);
    }

    private async Task<JsonObject> HandleStatusAsync(JsonObject? args, CancellationToken ct)
    {
        var statuses = await _hookInstallService.GetStatusAsync(cancellationToken: ct);
        var arr = new JsonArray(statuses.Select(s => (JsonNode)new JsonObject
        {
            ["client"] = s.Client,
            ["settingsFileExists"] = s.SettingsFileExists,
            ["hookInstalled"] = s.HookInstalled,
            ["endpointReachable"] = s.EndpointReachable,
            ["endpointWarning"] = s.EndpointWarning
        }).ToArray());
        return new JsonObject
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = arr.ToJsonString() })
        };
    }

    private static bool GetDryRun(JsonObject? args) =>
        args?["dry_run"]?.GetValue<bool>() ?? false;

    private static JsonObject BuildResult(bool success, string message, string? diff) =>
        new()
        {
            ["content"] = new JsonArray(new JsonObject
            {
                ["type"] = "text",
                ["text"] = $"{(success ? "✓" : "✗")} {message}" + (diff is not null ? $"\n{diff}" : "")
            }),
            ["isError"] = !success
        };

    private static JsonObject BuildError(string message) =>
        new()
        {
            ["content"] = new JsonArray(new JsonObject { ["type"] = "text", ["text"] = message }),
            ["isError"] = true
        };
}
```

- [ ] **Step 2: Register `nebula_setup` in the MCP tools list**

In `McpTransportHandler.cs`, find where tools are listed (the `tools/list` response). Add:

```json
{
  "name": "nebula_setup",
  "description": "Install, uninstall, or check the status of the Nebula Stop hook in AI client settings files (Claude Code, GitHub Copilot).",
  "inputSchema": {
    "type": "object",
    "properties": {
      "action": { "type": "string", "description": "install-hooks | uninstall-hooks | status" },
      "client": { "type": "string", "description": "claude (default) or copilot. Ignored for status." },
      "dry_run": { "type": "boolean", "description": "If true, returns diff without writing. Default false." }
    },
    "required": ["action"]
  }
}
```

Add `nebula_setup` to the tool dispatch switch in `McpTransportHandler.Tools.cs`:

```csharp
private const string NebulaSetupToolName = "nebula_setup";
// in the dispatch switch:
NebulaSetupToolName => await _nebulaSetupToolHandler.HandleAsync(arguments, cancellationToken),
```

Add `_nebulaSetupToolHandler` field and constructor param to `McpTransportHandler`.

- [ ] **Step 3: Build Core + Mcp** (Program.cs is updated in Task 8, so build Mcp last)

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj -q
```
Expected: `Build succeeded.`
> Note: `NebulaRAG.Mcp` still won't build until Task 8 wires `Program.cs`. That's expected.

- [ ] **Step 4: Commit**

```bash
git add src/NebulaRAG.Mcp/Tools/NebulaSetupToolHandler.cs \
        src/NebulaRAG.Core/Mcp/McpTransportHandler.cs \
        src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs
git commit -m "feat: add nebula_setup MCP tool with install-hooks, uninstall-hooks, status actions"
```

---

## Task 8: Wire everything in Program.cs + bump version

**Files:**
- Modify: `src/NebulaRAG.Mcp/Program.cs`
- Modify: `src/NebulaRAG.Core/Mcp/McpTransportHandler.cs` (version string)
- Modify: `tests/NebulaRAG.Tests/McpTransportHandlerContractTests.cs` (version assertion)

- [ ] **Step 1: Instantiate new services in `Program.cs`**

After the existing service instantiations (after `indexer`), add:

```csharp
var autoMemorySyncService = new AutoMemorySyncService(
    store,      // IAutoMemoryStore (PostgresRagStore implements it)
    indexer,    // IAutoMemoryIndexer (RagIndexer implements it)
    settings,
    loggerFactory.CreateLogger<AutoMemorySyncService>());

var hookInstallService = new HookInstallService(
    loggerFactory.CreateLogger<HookInstallService>());

var nebulaSetupToolHandler = new NebulaSetupToolHandler(hookInstallService);
```

Update the `McpTransportHandler` constructor call to include the two new services:

```csharp
var handler = new McpTransportHandler(
    queryService,
    managementService,
    sourcesManifestService,
    store,
    chunker,
    embeddingGenerator,
    indexer,
    settings,
    new HttpClient(),
    autoMemorySyncService,      // NEW
    nebulaSetupToolHandler,     // NEW
    loggerFactory.CreateLogger<McpTransportHandler>());
```

- [ ] **Step 2: Bump version to `0.3.83`**

In `McpTransportHandler.cs`, find the `initialize` response builder. The current version string is `"0.3.82"`:
```csharp
["version"] = "0.3.82"
```
Change to:
```csharp
["version"] = "0.3.83"
```

- [ ] **Step 3: Update version assertion in contract test**

In `McpTransportHandlerContractTests.cs`, change:
```csharp
Assert.Equal("0.2.0", response["result"]?["serverInfo"]?["version"]?.GetValue<string>());
```
to:
```csharp
Assert.Equal("0.3.83", response["result"]?["serverInfo"]?["version"]?.GetValue<string>());
```

Also add a `nebula_setup` tool presence assertion to the `tools/list` test (or the existing contract test if one exists):

```csharp
// In an appropriate tools/list test:
Assert.Contains(toolNames, t => t == "nebula_setup");
```

- [ ] **Step 4: Build and run full test suite**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet build src/ -q && dotnet test tests/ -v minimal
```
Expected: Build succeeded, all tests pass.

- [ ] **Step 5: Commit**

```bash
git add src/NebulaRAG.Mcp/Program.cs \
        src/NebulaRAG.Core/Mcp/McpTransportHandler.cs \
        tests/NebulaRAG.Tests/McpTransportHandlerContractTests.cs
git commit -m "feat: wire AutoMemorySyncService + nebula_setup into MCP server; bump version to 0.3.83"
```

---

## Task 9: Update AGENTS.md + README

**Files:**
- Modify: `AGENTS.md`
- Modify: `README.md`

- [ ] **Step 1: Update AGENTS.md session protocol**

In `AGENTS.md`, find the `Session Protocol` → `Session End` section (or add one if absent). Add:

```markdown
### Session End

1. Call `memory(action: "sync")` to bridge new auto-memory files into Nebula RAG, prune stale memories, and reindex dirty sources.
2. This is called automatically via the Claude Code `Stop` hook if installed via `nebula_setup(action: "install-hooks")`.
```

Also update the Required Tool Surface for `memory`:
```
- `memory` `action: "sync"` — run full maintenance pass at session end
```

- [ ] **Step 2: Update README.md**

Add a new `## Auto-Memory Sync` section describing:
- What `memory(action: "sync")` does (3 phases)
- How to install the Stop hook: `nebula_setup(action: "install-hooks")`
- How to check status: `nebula_setup(action: "status")`
- How to uninstall: `nebula_setup(action: "uninstall-hooks", dry_run: true)` then without `dry_run`

- [ ] **Step 3: Commit**

```bash
git add AGENTS.md README.md
git commit -m "docs: document auto-memory sync and nebula_setup hook management"
```

---

## Task 10: Final verification

- [ ] **Step 1: Full build + test**

```bash
cd /mnt/e/Projects/Personal/nebula-rag && dotnet build -q && dotnet test tests/ -v minimal
```
Expected: Build succeeded, all tests pass, 0 failures.

- [ ] **Step 2: Smoke test `nebula_setup status` via MCP**

If the Nebula MCP server is running locally, call:
```json
{ "method": "tools/call", "params": { "name": "nebula_setup", "arguments": { "action": "status" } } }
```
Expected: JSON with `claude` and `copilot` entries showing `settingsFileExists`, `hookInstalled`, `endpointReachable`.

- [ ] **Step 3: Smoke test `memory sync` via MCP**

```json
{ "method": "tools/call", "params": { "name": "memory", "arguments": { "action": "sync" } } }
```
Expected: JSON with `filesIngested`, `memoriesPruned`, `sourcesReindexed`, `errors`, `durationMs`.

- [ ] **Step 4: Final commit if anything was missed**

```bash
git status
# if any tracked changes remain:
git add -p && git commit -m "chore: final cleanup after auto-memory sync implementation"
```
