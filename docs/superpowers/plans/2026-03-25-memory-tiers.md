# Memory Tiers Implementation Plan

> **For agentic workers:** REQUIRED SUB-SKILL: Use superpowers:subagent-driven-development (recommended) or superpowers:executing-plans to implement this plan task-by-task. Steps use checkbox (`- [ ]`) syntax for tracking.

**Goal:** Add short-term (auto-pruned) and long-term (review-cycled) memory tiers to Nebula MCP with full agent and dashboard support.

**Architecture:** Add `tier` and `last_reviewed_at_utc` columns to the `memories` table via additive schema migration. Expose tier in all memory MCP actions; add `memory review` sub-actions. Update `AutoMemorySyncService` to prune by tier instead of tag. Add a review queue to the dashboard memory tab.

**Tech Stack:** C# / .NET 9, PostgreSQL + Npgsql, Blazor Server (dashboard), existing `McpTransportHandler` unified tool pattern.

**Spec:** `docs/superpowers/specs/2026-03-25-memory-tiers-design.md`

---

## File Map

| File | Change |
|------|--------|
| `src/NebulaRAG.Core/Models/MemoryModels.cs` | Add `Tier`, `LastReviewedAtUtc` to `MemoryRecord` + `MemorySearchResult`; add `MemoryTier` constants; add `MemoryReviewResult` record |
| `src/NebulaRAG.Core/Configuration/RagSettings.cs` | Add `ShortTermRetentionDays`, `LongTermReviewIntervalDays`; deprecate `RetentionDays` alias |
| `src/NebulaRAG.Core/Storage/PostgresRagStore.cs` | Schema migration; update all SELECT/INSERT/UPDATE to include tier columns; update `CreateMemoryAsync` (both overloads) + `UpdateMemoryAsync` (both overloads) signatures; add `ListMemoriesDueForReviewAsync`, `DeleteMemoriesByTierOlderThanAsync`; fix all 3 `MemoryRecord`/`MemorySearchResult` construction callsites |
| `src/NebulaRAG.Core/Services/IAutoMemoryStore.cs` | Add `DeleteMemoriesByTierOlderThanAsync` and `ListMemoriesDueForReviewAsync`; keep old prune method as deprecated |
| `src/NebulaRAG.Core/Services/RagManagementService.cs` | Thread optional `tier?` filter through `ListMemoriesAsync` and `SearchMemoriesAsync` overloads |
| `src/NebulaRAG.AddonHost/Controllers/RagApiController.cs` | Thread `tier?` filter through the controller's memory list/search action |
| `tests/NebulaRAG.Tests/MemorySearchResultRankerTests.cs` | Fix `MemorySearchResult` constructor callsites (add `Tier` + `LastReviewedAtUtc` args) |
| `src/NebulaRAG.Core/Services/AutoMemorySyncService.cs` | Switch Phase 2 to prune by tier; use `ShortTermRetentionDays` |
| `src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs` | Add `tier` param to `store`/`update`; add `recall` tier filter; add `review` action dispatch + 4 sub-actions |
| `src/NebulaRAG.AddonHost/Components/Pages/Dashboard.razor` | Add tier badge to memory list rows; add review queue sub-section with promote/demote/confirm/delete |
| `src/NebulaRAG.AddonHost/Components/Pages/Dashboard.razor.cs` | Wire review queue load, confirm, promote/demote handlers |
| `tests/NebulaRAG.Tests/MemoryTierTests.cs` | New test file — unit tests for tier validation, prune logic, review stamp |
| `nebula-rag/config.json` | Bump version to 0.3.80 |
| `nebula-rag/CHANGELOG.md` | Add 0.3.80 entry |

---

## Task 1: Model + Config changes

**Files:**
- Modify: `src/NebulaRAG.Core/Models/MemoryModels.cs`
- Modify: `src/NebulaRAG.Core/Configuration/RagSettings.cs`

- [ ] **Step 1: Write failing test for tier validation**

File: `tests/NebulaRAG.Tests/MemoryTierTests.cs`

```csharp
using Xunit;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Configuration;

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
    public void MemoryTier_IsValid_CorrectlyClassifies(string value, bool expected)
    {
        Assert.Equal(expected, MemoryTier.IsValid(value));
    }

    [Fact]
    public void AutoMemorySettings_RetentionDaysAlias_MapsToShortTerm()
    {
        var settings = new AutoMemorySettings { RetentionDays = 14 };
        Assert.Equal(14, settings.ShortTermRetentionDays);
    }

    [Fact]
    public void AutoMemorySettings_Defaults_AreCorrect()
    {
        var settings = new AutoMemorySettings();
        Assert.Equal(30, settings.ShortTermRetentionDays);
        Assert.Equal(90, settings.LongTermReviewIntervalDays);
    }

    [Fact]
    public void MemoryRecord_HasTierAndReviewFields()
    {
        var record = new MemoryRecord(1, "s1", null, "episodic", "content",
            [], DateTimeOffset.UtcNow, MemoryTier.ShortTerm, null);
        Assert.Equal(MemoryTier.ShortTerm, record.Tier);
        Assert.Null(record.LastReviewedAtUtc);
    }
}
```

- [ ] **Step 2: Run test to verify it fails**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ --filter "MemoryTierTests" --no-build 2>&1 | tail -20
```

Expected: compile error — `MemoryTier` not defined.

- [ ] **Step 3: Add `MemoryTier` constants class to `MemoryModels.cs`**

Add after the existing `MemoryDailyCount` record:

```csharp
/// <summary>Well-known memory tier values.</summary>
public static class MemoryTier
{
    public const string ShortTerm = "short_term";
    public const string LongTerm  = "long_term";

    private static readonly HashSet<string> Valid = [ShortTerm, LongTerm];

    /// <summary>Returns true if <paramref name="value"/> is a recognized tier string.</summary>
    public static bool IsValid(string? value) => value is not null && Valid.Contains(value);
}

/// <summary>A long-term memory returned from a review queue query.</summary>
/// <param name="Id">Memory identifier.</param>
/// <param name="SessionId">Session that created the memory.</param>
/// <param name="ProjectId">Optional project scope.</param>
/// <param name="Type">Memory type.</param>
/// <param name="Content">Memory content.</param>
/// <param name="Tags">Tags.</param>
/// <param name="CreatedAtUtc">When the memory was created.</param>
/// <param name="LastReviewedAtUtc">When the memory was last reviewed, or null if never.</param>
/// <param name="ReviewDueAtUtc">When review is due.</param>
/// <param name="ReviewStatus">current | due | overdue</param>
public sealed record MemoryReviewResult(
    long Id,
    string SessionId,
    string? ProjectId,
    string Type,
    string Content,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAtUtc,
    DateTimeOffset? LastReviewedAtUtc,
    DateTimeOffset ReviewDueAtUtc,
    string ReviewStatus);
```

- [ ] **Step 4: Update `MemoryRecord` and `MemorySearchResult` to include tier fields**

Replace both records in `MemoryModels.cs`:

```csharp
public sealed record MemoryRecord(
    long Id,
    string SessionId,
    string? ProjectId,
    string Type,
    string Content,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAtUtc,
    string Tier,
    DateTimeOffset? LastReviewedAtUtc);

public sealed record MemorySearchResult(
    long Id,
    string SessionId,
    string? ProjectId,
    string Type,
    string Content,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAtUtc,
    double Score,
    string Tier,
    DateTimeOffset? LastReviewedAtUtc);
```

- [ ] **Step 5: Update `AutoMemorySettings` in `RagSettings.cs`**

Replace the existing `AutoMemorySettings` class. **Important:** `ResolvedShortTermRetentionDays` is added here and used by `AutoMemorySyncService` in Task 3 — it must land in this commit.

```csharp
public sealed class AutoMemorySettings
{
    public string BaseDirectory { get; init; } = "~/.claude/projects";

    /// <summary>
    /// Age cutoff in days for short-term auto-pruning. 0 = disable pruning.
    /// Negative values are clamped to 0 with a startup warning.
    /// Default: 30
    /// </summary>
    public int ShortTermRetentionDays { get; init; } = 30;

    /// <summary>
    /// How often long-term memories need review (days). Minimum: 1.
    /// 0 or negative causes a startup validation error.
    /// Default: 90
    /// </summary>
    public int LongTermReviewIntervalDays { get; init; } = 90;

    /// <summary>Deprecated alias for ShortTermRetentionDays. Honoured on load.</summary>
    [Obsolete("Use ShortTermRetentionDays")]
    public int? RetentionDays
    {
        get => null;
        init => ShortTermRetentionDaysOverride = value;
    }

    // Internal: set when deprecated alias is used
    internal int? ShortTermRetentionDaysOverride { private get; init; }

    /// <summary>Resolved short-term retention: override wins over ShortTermRetentionDays.</summary>
    public int ResolvedShortTermRetentionDays =>
        ShortTermRetentionDaysOverride ?? ShortTermRetentionDays;

    internal void Validate(List<string> errors)
    {
        if (LongTermReviewIntervalDays <= 0)
            errors.Add("AutoMemory.LongTermReviewIntervalDays must be >= 1.");
        if (ResolvedShortTermRetentionDays < 0)
            errors.Add("AutoMemory.ShortTermRetentionDays must be >= 0 (0 = disabled).");
    }
}
```

- [ ] **Step 6: Run tests to verify they pass**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ --filter "MemoryTierTests" 2>&1 | tail -20
```

Expected: all 5 tests pass.

- [ ] **Step 6b: Fix `MemorySearchResultRankerTests.cs` broken callsites**

Open `tests/NebulaRAG.Tests/MemorySearchResultRankerTests.cs` and find every `new MemorySearchResult(...)` constructor call (there will be ~3). Each one currently passes 8 positional args. Add the two new trailing args:

```csharp
// Before (8 args):
new MemorySearchResult(id, sessionId, projectId, type, content, tags, createdAt, score)

// After (10 args — add Tier and LastReviewedAtUtc):
new MemorySearchResult(id, sessionId, projectId, type, content, tags, createdAt, score,
    MemoryTier.ShortTerm, null)
```

Run tests to confirm they compile and pass:

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ 2>&1 | tail -20
```

Expected: all tests pass.

- [ ] **Step 7: Commit**

```bash
git add src/NebulaRAG.Core/Models/MemoryModels.cs \
        src/NebulaRAG.Core/Configuration/RagSettings.cs \
        tests/NebulaRAG.Tests/MemoryTierTests.cs \
        tests/NebulaRAG.Tests/MemorySearchResultRankerTests.cs
git commit -m "feat: add MemoryTier constants, MemoryReviewResult model, tier fields on MemoryRecord; update AutoMemorySettings"
```

---

## Task 2: Database schema migration + storage layer

**Files:**
- Modify: `src/NebulaRAG.Core/Storage/PostgresRagStore.cs`
- Modify: `src/NebulaRAG.Core/Services/IAutoMemoryStore.cs`

- [ ] **Step 1: Write failing tests for new storage methods**

Add to `tests/NebulaRAG.Tests/MemoryTierTests.cs`:

```csharp
// Integration tests require a live DB — these validate the interface contract shape only.
// Full integration tests are covered by running the server against a dev DB.

[Fact]
public void IAutoMemoryStore_HasDeleteByTierMethod()
{
    // Verify the interface declares the new method (compile-time check)
    var methods = typeof(IAutoMemoryStore).GetMethods();
    Assert.Contains(methods, m => m.Name == "DeleteMemoriesByTierOlderThanAsync");
}
```

- [ ] **Step 2: Run to verify it fails**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ --filter "IAutoMemoryStore_HasDeleteByTierMethod" 2>&1 | tail -10
```

Expected: FAIL — method not found.

- [ ] **Step 3: Add schema migration for tier columns**

In `PostgresRagStore.cs`, find the `InitializeSchemaAsync` method. After the existing `ALTER TABLE memories` block (around line 79), add:

```csharp
// Memory tier columns (v0.3.80)
ALTER TABLE memories ADD COLUMN IF NOT EXISTS tier TEXT NOT NULL DEFAULT 'short_term';
ALTER TABLE memories ADD COLUMN IF NOT EXISTS last_reviewed_at_utc TIMESTAMPTZ NULL;
CREATE INDEX IF NOT EXISTS ix_memories_tier ON memories (tier);
CREATE INDEX IF NOT EXISTS ix_memories_review ON memories (tier, last_reviewed_at_utc) WHERE tier = 'long_term';
```

- [ ] **Step 4: Update all SELECT queries to include new columns**

Find every `SELECT` from `memories` in `PostgresRagStore.cs` and add `tier, last_reviewed_at_utc` to the column list. Update each `new MemoryRecord(...)` and `new MemorySearchResult(...)` constructor call to pass `tier` and `lastReviewedAtUtc` from the reader:

```csharp
// After existing columns, add:
Tier: reader.GetString(/* tier column index */),
LastReviewedAtUtc: reader.IsDBNull(/* last_reviewed_at_utc index */) ? null : reader.GetFieldValue<DateTimeOffset>(/* index */)
```

- [ ] **Step 5: Update `CreateMemoryAsync` signatures (both overloads)**

Add `string? tier = null` as the last parameter before `CancellationToken` in both overloads. The single-overload (no `projectId`) should forward it to the full overload:

```csharp
// Short overload
public Task<long> CreateMemoryAsync(string? sessionId, string type, string content,
    IReadOnlyList<string> tags, IReadOnlyList<float> embedding, string? tier = null,
    CancellationToken cancellationToken = default)
    => CreateMemoryAsync(sessionId, projectId: null, type, content, tags, embedding, tier, cancellationToken);

// Full overload — add tier to INSERT
INSERT INTO memories (session_id, project_id, type, content, embedding, tags, tier)
VALUES (@sessionId, @projectId, @type, @content, @embedding, @tags, @tier)
// Add parameter: cmd.Parameters.AddWithValue("tier", tier ?? MemoryTier.ShortTerm);
```

- [ ] **Step 6: Update `UpdateMemoryAsync` signatures (both overloads) + interface**

Add `string? tier = null` and `bool stampReviewed = false` parameters to `UpdateMemoryAsync`. Build the SET clause dynamically:

```csharp
public async Task<bool> UpdateMemoryAsync(long memoryId, string? type, string? content,
    IReadOnlyList<string>? tags, IReadOnlyList<float>? embedding,
    bool stampReviewed = false, string? tier = null,
    CancellationToken cancellationToken = default)
{
    var setClauses = new List<string>();
    if (type is not null)    setClauses.Add("type = @type");
    if (content is not null) setClauses.Add("content = @content, embedding = @embedding");
    if (tags is not null)    setClauses.Add("tags = @tags");
    if (tier is not null)
    {
        setClauses.Add("tier = @tier");
        // Only clear the review stamp on tier change if we're NOT also stamping it now.
        // (Prevents conflicting SET clauses: "last_reviewed_at_utc = NULL, last_reviewed_at_utc = NOW()")
        if (!stampReviewed)
            setClauses.Add("last_reviewed_at_utc = NULL");
    }
    if (stampReviewed)       setClauses.Add("last_reviewed_at_utc = NOW()");
    if (setClauses.Count == 0) return false;
    var sql = $"UPDATE memories SET {string.Join(", ", setClauses)} WHERE id = @id";
    // ... add parameters and execute
}
```

Also update the existing `UpdateMemoryAsync` signature in `PostgresRagStore` to match, and update the caller at `McpTransportHandler.Tools.cs:~915` to pass the new params explicitly (no `stampReviewed`, no `tier` — existing behaviour unchanged).

- [ ] **Step 7: Add `DeleteMemoriesByTierOlderThanAsync` to `IAutoMemoryStore` and implement**

```csharp
/// <summary>Deletes short-term memory rows older than <paramref name="cutoff"/>.</summary>
Task<int> DeleteMemoriesByTierOlderThanAsync(string tier, DateTimeOffset cutoff, CancellationToken cancellationToken = default);
```

Keep `DeleteMemoriesByTagOlderThanAsync` but mark it `[Obsolete]`.

- [ ] **Step 8: Implement `DeleteMemoriesByTierOlderThanAsync` in `PostgresRagStore`**

```csharp
public async Task<int> DeleteMemoriesByTierOlderThanAsync(string tier, DateTimeOffset cutoff, CancellationToken cancellationToken = default)
{
    const string sql = "DELETE FROM memories WHERE tier = @tier AND created_at < @cutoff";
    await using var connection = await OpenConnectionAsync(cancellationToken);
    await using var cmd = new NpgsqlCommand(sql, connection);
    cmd.Parameters.AddWithValue("tier", tier);
    cmd.Parameters.AddWithValue("cutoff", cutoff);
    return await cmd.ExecuteNonQueryAsync(cancellationToken);
}
```

- [ ] **Step 8: Implement `ListMemoriesDueForReviewAsync` in `PostgresRagStore`**

```csharp
// Interface
Task<IReadOnlyList<MemoryReviewResult>> ListMemoriesDueForReviewAsync(int reviewIntervalDays, int limit = 50, CancellationToken cancellationToken = default);

// Implementation in PostgresRagStore
public async Task<IReadOnlyList<MemoryReviewResult>> ListMemoriesDueForReviewAsync(int reviewIntervalDays, int limit = 50, CancellationToken cancellationToken = default)
{
    const string sql = """
        SELECT id, session_id, project_id, type, content, tags, created_at, last_reviewed_at_utc,
               COALESCE(last_reviewed_at_utc, created_at) + (@intervalDays * INTERVAL '1 day') AS review_due_at
        FROM memories
        WHERE tier = 'long_term'
          AND COALESCE(last_reviewed_at_utc, created_at) + (@intervalDays * INTERVAL '1 day') <= NOW()
        ORDER BY review_due_at ASC
        LIMIT @limit
        """;
    await using var connection = await OpenConnectionAsync(cancellationToken);
    await using var cmd = new NpgsqlCommand(sql, connection);
    cmd.Parameters.AddWithValue("intervalDays", reviewIntervalDays);
    cmd.Parameters.AddWithValue("limit", limit);
    await using var reader = await cmd.ExecuteReaderAsync(cancellationToken);
    var rows = new List<MemoryReviewResult>();
    while (await reader.ReadAsync(cancellationToken))
    {
        var reviewDue = reader.GetFieldValue<DateTimeOffset>(8);
        var daysOverdue = (DateTimeOffset.UtcNow - reviewDue).TotalDays;
        var status = daysOverdue > 30 ? "overdue" : "due";
        rows.Add(new MemoryReviewResult(
            Id: reader.GetInt64(0),
            SessionId: reader.IsDBNull(1) ? "" : reader.GetString(1),
            ProjectId: reader.IsDBNull(2) ? null : reader.GetString(2),
            Type: reader.GetString(3),
            Content: reader.GetString(4),
            Tags: reader.IsDBNull(5) ? [] : reader.GetFieldValue<string[]>(5),
            CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(6),
            LastReviewedAtUtc: reader.IsDBNull(7) ? null : reader.GetFieldValue<DateTimeOffset>(7),
            ReviewDueAtUtc: reviewDue,
            ReviewStatus: status));
    }
    return rows;
}
```

- [ ] **Step 9: Fix all broken `MemoryRecord`/`MemorySearchResult` callsites in `PostgresRagStore.cs`**

After adding the columns to SELECT queries, find all constructor calls and add the two new fields. There are 3 callsites:

| ~Line | Record | Fix |
|-------|--------|-----|
| ~1357 | `new MemoryRecord(...)` | Add `Tier: reader.GetString(N), LastReviewedAtUtc: reader.IsDBNull(N+1) ? null : reader.GetFieldValue<DateTimeOffset>(N+1)` |
| ~456  | `new MemorySearchResult(...)` (lexical search) | Same — add Tier + LastReviewedAtUtc from reader |
| ~1445 | `new MemorySearchResult(...)` (semantic search) | Same |

Also fix the callsite in `McpTransportHandler.Tools.cs` at ~line 791 (list fallback path that constructs `MemorySearchResult` from a `MemoryRecord`):

```csharp
// Before:
new MemorySearchResult(memory.Id, memory.SessionId, memory.ProjectId, memory.Type,
    memory.Content, memory.Tags, memory.CreatedAtUtc, 0d)

// After:
new MemorySearchResult(memory.Id, memory.SessionId, memory.ProjectId, memory.Type,
    memory.Content, memory.Tags, memory.CreatedAtUtc, 0d, memory.Tier, memory.LastReviewedAtUtc)
```

- [ ] **Step 10: Thread optional `tier?` filter through `ListMemoriesAsync` and `SearchMemoriesAsync`**

In `PostgresRagStore.cs`, add `string? tier = null` to both `ListMemoriesAsync` overloads and both `SearchMemoriesAsync` overloads. When non-null, append `AND tier = @tier` to the WHERE clause.

In `RagManagementService.cs`, thread the same `tier?` parameter through its `ListMemoriesAsync` and `SearchMemoriesAsync` wrapper methods.

In `RagApiController.cs`, add `[FromQuery] string? tier = null` to the memory list/search action methods and pass it down to the management service.

- [ ] **Step 11: Run tests**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ --filter "MemoryTierTests" 2>&1 | tail -20
```

Expected: all tests pass including the interface check.

- [ ] **Step 11: Build full solution to catch any broken callers**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet build 2>&1 | grep -E "error|warning" | grep -v "Obsolete" | head -30
```

Expected: 0 errors. Fix any `MemoryRecord`/`MemorySearchResult` constructor callers that break due to the new positional fields.

- [ ] **Step 12: Commit**

```bash
git add src/NebulaRAG.Core/Storage/PostgresRagStore.cs \
        src/NebulaRAG.Core/Services/IAutoMemoryStore.cs \
        tests/NebulaRAG.Tests/MemoryTierTests.cs
git commit -m "feat: add tier + last_reviewed_at_utc schema migration, storage methods for tier prune and review queue"
```

---

## Task 3: AutoMemorySyncService — prune by tier

**Files:**
- Modify: `src/NebulaRAG.Core/Services/AutoMemorySyncService.cs`

- [ ] **Step 1: Write failing test for tier-based pruning**

Add to `tests/NebulaRAG.Tests/MemoryTierTests.cs`:

```csharp
[Fact]
public async Task AutoMemorySyncService_PrunePhase_UsesShortTermRetentionDays()
{
    // Arrange: spy on which method gets called with which args
    var store = new FakeAutoMemoryStore();
    var settings = new RagSettings
    {
        AutoMemory = new AutoMemorySettings { ShortTermRetentionDays = 7 }
    };
    var svc = new AutoMemorySyncService(store, new FakeAutoMemoryIndexer(), settings,
        Microsoft.Extensions.Logging.Abstractions.NullLogger<AutoMemorySyncService>.Instance);

    // Act
    await svc.SyncAsync();

    // Assert: DeleteByTier was called, not DeleteByTag
    Assert.True(store.DeleteByTierWasCalled);
    Assert.Equal(MemoryTier.ShortTerm, store.LastDeletedTier);
    Assert.False(store.DeleteByTagWasCalled);
}
```

Add minimal `FakeAutoMemoryStore` and `FakeAutoMemoryIndexer` stubs in the test file that track calls.

- [ ] **Step 2: Run to verify it fails**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ --filter "AutoMemorySyncService_PrunePhase" 2>&1 | tail -15
```

Expected: FAIL — `DeleteByTierWasCalled` is false.

- [ ] **Step 3: Update `PruneStaleMemoriesAsync` in `AutoMemorySyncService.cs`**

Replace:
```csharp
private async Task<int> PruneStaleMemoriesAsync(CancellationToken ct)
{
    if (_settings.AutoMemory.RetentionDays == 0) return 0;
    var cutoff = DateTimeOffset.UtcNow.AddDays(-_settings.AutoMemory.RetentionDays);
    return await _store.DeleteMemoriesByTagOlderThanAsync("auto-memory", cutoff, ct);
}
```

With:
```csharp
private async Task<int> PruneStaleMemoriesAsync(CancellationToken ct)
{
    var retentionDays = _settings.AutoMemory.ResolvedShortTermRetentionDays;
    if (retentionDays == 0) return 0;
    if (retentionDays < 0)
    {
        _logger.LogWarning("AutoMemory.ShortTermRetentionDays is negative ({Days}); pruning disabled.", retentionDays);
        return 0;
    }
    var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
    return await _store.DeleteMemoriesByTierOlderThanAsync(MemoryTier.ShortTerm, cutoff, ct);
}
```

- [ ] **Step 4: Run tests**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ --filter "MemoryTierTests" 2>&1 | tail -20
```

Expected: all pass.

- [ ] **Step 5: Commit**

```bash
git add src/NebulaRAG.Core/Services/AutoMemorySyncService.cs \
        tests/NebulaRAG.Tests/MemoryTierTests.cs
git commit -m "feat: switch AutoMemorySyncService Phase 2 to prune by tier instead of tag"
```

---

## Task 4: MCP tool — tier param + review actions

**Files:**
- Modify: `src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs`

- [ ] **Step 1: Write failing test for tier validation in MCP layer**

Add to `tests/NebulaRAG.Tests/MemoryTierTests.cs`:

```csharp
[Theory]
[InlineData("short_term")]
[InlineData("long_term")]
[InlineData(null)]  // null = default = short_term, valid
public void MemoryTier_ValidOrNull_ShouldNotError(string? tier)
{
    // Validates the helper used by McpTransportHandler
    var result = tier is null || MemoryTier.IsValid(tier);
    Assert.True(result);
}

[Theory]
[InlineData("medium")]
[InlineData("LONG_TERM")]
[InlineData("")]
public void MemoryTier_Invalid_ShouldError(string tier)
{
    Assert.False(MemoryTier.IsValid(tier));
}
```

- [ ] **Step 2: Run to verify they pass (these are pure logic tests)**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ --filter "MemoryTier_Valid" 2>&1 | tail -10
```

Expected: pass (these validate existing `MemoryTier.IsValid` from Task 1).

- [ ] **Step 3: Update `ExecuteMemoryToolAsync` — add `review` to dispatch**

Find the action switch (~line 166) and add:

```csharp
"review" => await ExecuteMemoryReviewToolAsync(arguments, cancellationToken),
```

Update the error message:
```csharp
_ => BuildToolResult("Unsupported memory action. Use: store, recall, list, update, delete, sync, or review.", isError: true)
```

- [ ] **Step 4: Add `tier` validation helper**

Add a private method to `McpTransportHandler.Tools.cs`:

```csharp
private static string? ValidateTier(string? tier, out JsonObject? error)
{
    error = null;
    if (tier is null) return MemoryTier.ShortTerm; // default
    if (!MemoryTier.IsValid(tier))
    {
        error = BuildToolResult("Invalid tier value. Use: short_term or long_term.", isError: true);
        return null;
    }
    return tier;
}
```

- [ ] **Step 5: Update `ExecuteMemoryStoreToolAsync` to accept `tier`**

After extracting existing params, add:
```csharp
var rawTier = arguments?["tier"]?.GetValue<string?>();
var tier = ValidateTier(rawTier, out var tierError);
if (tierError is not null) return tierError;
```

Pass `tier` to `CreateMemoryAsync`.

- [ ] **Step 6: Update `ExecuteMemoryUpdateToolAsync` to accept `tier`**

Same pattern: extract `tier`, validate, pass to `UpdateMemoryAsync` with `stampReviewed: false`.

- [ ] **Step 7: Update `ExecuteMemoryRecallToolAsync` to support tier filter**

Extract optional `tier` filter from arguments:
```csharp
var tierFilter = arguments?["tier"]?.GetValue<string?>();
if (tierFilter is not null && !MemoryTier.IsValid(tierFilter))
    return BuildToolResult("Invalid tier value. Use: short_term or long_term.", isError: true);
```

Pass `tierFilter` to `SearchMemoriesAsync` / `ListMemoriesAsync` (add optional `tier` param to those methods). If null, both tiers are returned.

Add `tier` to each result object:
```csharp
["tier"] = memory.Tier,
```

- [ ] **Step 8: Add `ExecuteMemoryReviewToolAsync`**

```csharp
private async Task<JsonObject> ExecuteMemoryReviewToolAsync(JsonObject? arguments, CancellationToken cancellationToken)
{
    var subAction = arguments?["subAction"]?.GetValue<string?>()?.ToLowerInvariant();
    return subAction switch
    {
        "list"    => await ExecuteMemoryReviewListAsync(arguments, cancellationToken),
        "confirm" => await ExecuteMemoryReviewConfirmAsync(arguments, cancellationToken),
        "update"  => await ExecuteMemoryReviewUpdateAsync(arguments, cancellationToken),
        "delete"  => await ExecuteMemoryReviewDeleteAsync(arguments, cancellationToken),
        _ => BuildToolResult("subAction is required. Use: list, confirm, update, or delete.", isError: true)
    };
}

private async Task<JsonObject> ExecuteMemoryReviewListAsync(JsonObject? arguments, CancellationToken ct)
{
    var limit = arguments?["limit"]?.GetValue<int?>() ?? 20;
    var results = await _store.ListMemoriesDueForReviewAsync(_settings.AutoMemory.LongTermReviewIntervalDays, limit, ct);
    var items = new JsonArray();
    foreach (var m in results)
    {
        items.Add(new JsonObject
        {
            ["id"] = m.Id,
            ["content"] = m.Content,
            ["type"] = m.Type,
            ["tags"] = JsonSerializer.SerializeToNode(m.Tags),
            ["createdAtUtc"] = m.CreatedAtUtc.ToString("O"),
            ["lastReviewedAtUtc"] = m.LastReviewedAtUtc?.ToString("O"),
            ["reviewDueAtUtc"] = m.ReviewDueAtUtc.ToString("O"),
            ["reviewStatus"] = m.ReviewStatus
        });
    }
    return BuildToolResult($"{results.Count} memories due for review.", new JsonObject { ["memories"] = items });
}

private async Task<JsonObject> ExecuteMemoryReviewConfirmAsync(JsonObject? arguments, CancellationToken ct)
{
    var memoryId = arguments?["memoryId"]?.GetValue<long?>();
    if (memoryId is null) return BuildToolResult("memoryId is required.", isError: true);
    var updated = await _store.UpdateMemoryAsync(memoryId.Value, type: null, content: null, tags: null, embedding: null, stampReviewed: true, ct);
    return BuildToolResult(updated ? "Memory review confirmed." : "Memory not found.", new JsonObject { ["updated"] = updated });
}

private async Task<JsonObject> ExecuteMemoryReviewUpdateAsync(JsonObject? arguments, CancellationToken ct)
{
    var memoryId = arguments?["memoryId"]?.GetValue<long?>();
    if (memoryId is null) return BuildToolResult("memoryId is required.", isError: true);
    var content = arguments?["content"]?.GetValue<string?>();
    IReadOnlyList<float>? embedding = content is not null ? await EmbedAsync(content, ct) : null;
    var updated = await _store.UpdateMemoryAsync(memoryId.Value, type: null, content, tags: null, embedding, stampReviewed: true, ct);
    return BuildToolResult(updated ? "Memory updated and review confirmed." : "Memory not found.", new JsonObject { ["updated"] = updated });
}

private async Task<JsonObject> ExecuteMemoryReviewDeleteAsync(JsonObject? arguments, CancellationToken ct)
{
    var memoryId = arguments?["memoryId"]?.GetValue<long?>();
    if (memoryId is null) return BuildToolResult("memoryId is required.", isError: true);
    var deleted = await _store.DeleteMemoryAsync(memoryId.Value, ct);
    return BuildToolResult(deleted ? "Memory deleted." : "Memory not found.", new JsonObject { ["deleted"] = deleted });
}
```

- [ ] **Step 9: Build to catch errors**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet build src/NebulaRAG.Core/ 2>&1 | grep -E "^.*error" | head -20
```

Expected: 0 errors.

- [ ] **Step 10: Run all tests**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ 2>&1 | tail -20
```

Expected: all pass.

- [ ] **Step 11: Commit**

```bash
git add src/NebulaRAG.Core/Mcp/McpTransportHandler.Tools.cs \
        tests/NebulaRAG.Tests/MemoryTierTests.cs
git commit -m "feat: add tier param to memory store/update/recall; add memory review MCP sub-actions"
```

---

## Task 5: Dashboard — tier badges + review queue

**Files:**
- Modify: `src/NebulaRAG.AddonHost/Components/Pages/Dashboard.razor`
- Modify: `src/NebulaRAG.AddonHost/Components/Pages/Dashboard.razor.cs`

- [ ] **Step 1: Add tier badge to memory list rows in `Dashboard.razor`**

Find the memory list row rendering in the Memory tab. After the type badge, add:

```razor
<span class="badge @(memory.Tier == "long_term" ? "badge-primary" : "badge-ghost") badge-sm">
    @(memory.Tier == "long_term" ? "LT" : "ST")
</span>
```

- [ ] **Step 2: Add review queue sub-section to `Dashboard.razor`**

In the Memory tab, above the main ledger, add:

```razor
@if (_reviewQueueCount > 0)
{
    <div class="alert alert-warning mb-4">
        <span class="font-bold">@_reviewQueueCount long-term @(_reviewQueueCount == 1 ? "memory" : "memories") due for review</span>
        <button class="btn btn-sm btn-outline ml-4" @onclick="ToggleReviewQueue">
            @(_showReviewQueue ? "Hide" : "Show") Queue
        </button>
    </div>
}

@if (_showReviewQueue && _reviewQueue.Count > 0)
{
    <div class="card bg-base-200 mb-6">
        <div class="card-body p-4">
            <div class="flex justify-between items-center mb-3">
                <h3 class="font-semibold">Review Queue</h3>
                <button class="btn btn-xs btn-ghost" @onclick="MarkAllReviewedAsync">Mark all reviewed</button>
            </div>
            @foreach (var item in _reviewQueue)
            {
                <div class="border border-base-300 rounded p-3 mb-2 text-sm">
                    <div class="flex justify-between items-start gap-2">
                        <p class="flex-1">@item.Content</p>
                        <span class="badge @(item.ReviewStatus == "overdue" ? "badge-error" : "badge-warning") badge-sm shrink-0">
                            @item.ReviewStatus
                        </span>
                    </div>
                    <div class="text-xs text-base-content/50 mt-1">
                        Created @item.CreatedAtUtc.ToString("yyyy-MM-dd") ·
                        Last reviewed: @(item.LastReviewedAtUtc?.ToString("yyyy-MM-dd") ?? "never") ·
                        Due: @item.ReviewDueAtUtc.ToString("yyyy-MM-dd")
                    </div>
                    <div class="flex gap-2 mt-2">
                        <button class="btn btn-xs btn-ghost" @onclick="() => ConfirmReviewAsync(item.Id)">Confirm</button>
                        <button class="btn btn-xs btn-ghost" @onclick="() => BeginEditReview(item)">Edit</button>
                        <button class="btn btn-xs btn-ghost text-error" @onclick="() => DeleteMemoryAsync(item.Id)">Delete</button>
                        <button class="btn btn-xs btn-outline" @onclick="() => DemoteToShortTermAsync(item.Id)">Demote to ST</button>
                    </div>
                </div>
            }
        </div>
    </div>
}
```

Also add a "Promote to LT" button to each memory row in the main list when `memory.Tier == "short_term"`.

- [ ] **Step 3: Wire up review queue handlers in `Dashboard.razor.cs`**

Add fields:
```csharp
private List<MemoryReviewResult> _reviewQueue = [];
private int _reviewQueueCount = 0;
private bool _showReviewQueue = false;
```

Add methods:
```csharp
private async Task LoadReviewQueueAsync()
{
    var results = await _store.ListMemoriesDueForReviewAsync(
        _settings.AutoMemory.LongTermReviewIntervalDays, limit: 50);
    _reviewQueue = results.ToList();
    _reviewQueueCount = _reviewQueue.Count;
}

private void ToggleReviewQueue() => _showReviewQueue = !_showReviewQueue;

private async Task ConfirmReviewAsync(long id)
{
    await _store.UpdateMemoryAsync(id, null, null, null, null, stampReviewed: true);
    await LoadReviewQueueAsync();
    StateHasChanged();
}

private async Task MarkAllReviewedAsync()
{
    foreach (var item in _reviewQueue)
        await _store.UpdateMemoryAsync(item.Id, null, null, null, null, stampReviewed: true);
    await LoadReviewQueueAsync();
    StateHasChanged();
}

private async Task DemoteToShortTermAsync(long id)
{
    await _store.UpdateMemoryAsync(id, null, null, null, null, stampReviewed: false, tier: MemoryTier.ShortTerm);
    await LoadReviewQueueAsync();
    StateHasChanged();
}

private async Task PromoteToLongTermAsync(long id)
{
    await _store.UpdateMemoryAsync(id, null, null, null, null, stampReviewed: false, tier: MemoryTier.LongTerm);
    StateHasChanged();
}
```

Call `await LoadReviewQueueAsync()` inside `OnAfterRenderAsync` (or wherever the memory tab data loads).

- [ ] **Step 4: Build full solution**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet build 2>&1 | grep -E "^.*error" | head -20
```

Expected: 0 errors.

- [ ] **Step 5: Run all tests**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet test tests/NebulaRAG.Tests/ 2>&1 | tail -20
```

Expected: all pass.

- [ ] **Step 6: Commit**

```bash
git add src/NebulaRAG.AddonHost/Components/Pages/Dashboard.razor \
        src/NebulaRAG.AddonHost/Components/Pages/Dashboard.razor.cs
git commit -m "feat: add tier badges and review queue to dashboard memory tab"
```

---

## Task 6: Version bump, changelog, push

**Files:**
- Modify: `nebula-rag/config.json`
- Modify: `nebula-rag/CHANGELOG.md`

- [ ] **Step 1: Bump version in `nebula-rag/config.json`**

Change `"version": "0.3.79"` → `"version": "0.3.80"`.

- [ ] **Step 2: Add changelog entry**

Add to top of `nebula-rag/CHANGELOG.md` (after the format line):

```markdown
## [0.3.80] - 2026-03-25

- Added `short_term` / `long_term` memory tiers. All existing memories default to `short_term`.
- `memory store` and `memory update` now accept an optional `tier` parameter. Invalid values return an explicit error.
- `memory recall` now accepts an optional `tier` filter; default returns both tiers.
- Added `memory review` MCP action with sub-actions: `list`, `confirm`, `update`, `delete`.
- Auto-pruning in `AutoMemorySyncService` now targets `tier = short_term` instead of `auto-memory` tag.
- Added `ShortTermRetentionDays` (default 30) and `LongTermReviewIntervalDays` (default 90) config keys.
- `RetentionDays` is now a deprecated alias for `ShortTermRetentionDays` — existing configs continue to work.
- Dashboard memory tab: tier badges (ST/LT) on all rows, promote/demote buttons, and a review queue with overdue badge count, per-row confirm/edit/delete/demote, and bulk "mark all reviewed".
```

- [ ] **Step 3: Final build + test**

```bash
cd /home/mark/projects/personal/nebula-rag
dotnet build && dotnet test tests/NebulaRAG.Tests/ 2>&1 | tail -30
```

Expected: build succeeds, all tests pass.

- [ ] **Step 4: Commit and push**

```bash
git add nebula-rag/config.json nebula-rag/CHANGELOG.md
git commit -m "chore: bump version to 0.3.80 — memory tiers"
git push
```

---

## Verification Checklist

After all tasks are complete, manually verify:

- [ ] `memory store` with `tier:"long_term"` stores correctly and appears in `memory recall` without a filter
- [ ] `memory store` with `tier:"medium"` returns the error message
- [ ] `memory review list` returns only overdue long-term memories
- [ ] `memory review confirm` stamps `last_reviewed_at_utc` and the memory disappears from the review queue
- [ ] `memory update` with `tier:"long_term"` promotes a short-term memory and clears `last_reviewed_at_utc`
- [ ] `AutoMemorySyncService` sync does not prune long-term memories regardless of age
- [ ] Dashboard shows ST/LT badge on memory rows
- [ ] Dashboard review queue badge shows correct overdue count
- [ ] "Mark all reviewed" clears the queue
