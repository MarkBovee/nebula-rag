# Nebula Auto-Memory Sync & Hook Setup — Design Spec

**Date:** 2026-03-24
**Status:** Approved
**Scope:** NebulaRAG.Core + NebulaRAG.Mcp

---

## Problem

Claude Code v2.1.59 introduced **Auto Memory** — Claude automatically accumulates project knowledge in `~/.claude/projects/*/memory/*.md` across sessions. This knowledge is not queryable by Nebula, not deduplicated, and never cleaned up. Separately, RAG sources can drift out of sync with files on disk, and connecting the Stop hook requires manual settings editing.

---

## Goals

1. Bridge Claude Code auto-memory files into the Nebula RAG index — automatically, on session end.
2. Prune stale Nebula memory entries to prevent accumulation of irrelevant context.
3. Reindex dirty RAG sources incrementally (hash-based, not full rebuild).
4. Provide a first-class MCP tool to install/uninstall/inspect the Stop hook in client settings files.

---

## Non-Goals

- Full RAG reindex on every sync (manual `rag_admin` action for that).
- Relevance-based memory pruning (Phase 2, requires telemetry).
- Polyglot sandbox execution.
- Support for clients beyond Claude Code and GitHub Copilot at launch.

---

## Architecture

Two MCP surface changes:

| Tool | Change |
|------|--------|
| `memory` | New `sync` action |
| `nebula_setup` | New top-level tool with `install-hooks`, `uninstall-hooks`, `status` actions |

Both backed by new Core services. The Claude Code `Stop` hook calls `memory(action: "sync")` automatically.

---

## memory(action: "sync")

### New Core Service: `AutoMemorySyncService`

Location: `NebulaRAG.Core/Services/AutoMemorySyncService.cs`

Executes three sequential phases. Each phase is fault-isolated — a failure adds to `errors[]` and execution continues.

#### Phase 1: Auto-Memory Bridge

1. Glob `~/.claude/projects/*/memory/*.md` (configurable base via `AutoMemory.BaseDirectory`, default `~/.claude/projects`).
2. For each file, compute SHA-256 hash.
3. Compare against `nebula_sync_state` table (`file_path`, `last_hash`, `synced_at`).
4. New or changed files → ingest via `RagIndexer` with source tag `auto-memory:{project-slug}` where `project-slug` is the parent directory name.
5. Update `nebula_sync_state` with new hash and timestamp.
6. Unchanged files → skip.

#### Phase 2: Stale Memory Pruning

1. Query Nebula memory entries where `tags` contains `auto-memory` AND `created_at < NOW() - RetentionDays`.
2. Delete matching entries via existing memory store.
3. `RetentionDays` configurable via `ragsettings.json` → `AutoMemory.RetentionDays` (default: `30`).

#### Phase 3: Dirty RAG Source Reindex

1. Query all tracked sources from `RagSourcesManifestService`.
2. For each source backed by a file path: hash current content on disk.
3. Compare against stored hash in manifest.
4. Dirty sources (hash mismatch or file missing) → re-chunk and re-embed via `RagIndexer`.
5. Clean sources → skip.
6. Missing files → log warning, add to `errors[]`, do not delete source (safety — could be a temp unmount).

### Return Value: `SyncSummary`

```csharp
public record SyncSummary(
    int FilesIngested,
    int MemoriesPruned,
    int SourcesReindexed,
    IReadOnlyList<string> Errors,
    long DurationMs
);
```

### Configuration (`ragsettings.json`)

```json
{
  "AutoMemory": {
    "BaseDirectory": "~/.claude/projects",
    "RetentionDays": 30
  }
}
```

---

## nebula_setup Tool

### New Core Service: `HookInstallService`

Location: `NebulaRAG.Core/Services/HookInstallService.cs`

#### Actions

**`install-hooks`**
- Resolve target settings file by `client` (`claude` → `~/.claude/settings.json`, `copilot` → OS-resolved path).
- Read existing JSON, locate or create `hooks.Stop` array.
- Check for existing Nebula hook entry (idempotent — no duplicate if already present).
- Inject: `{ "matcher": "", "hooks": [{ "type": "command", "command": "nebula-rag memory sync" }] }`
- If `dry_run: true` → return diff string, do not write.
- If `dry_run: false` → write to temp file, rename to target (atomic write).

**`uninstall-hooks`**
- Resolve target settings file.
- Remove the Nebula hook entry if present.
- Dry-run support identical to install.

**`status`**
- For each supported client: check if settings file exists, whether Nebula hook is present, whether the Nebula MCP HTTP endpoint is reachable.
- Returns structured status per client.

#### Supported Clients

| Client | Settings Path |
|--------|--------------|
| `claude` | `~/.claude/settings.json` |
| `copilot` | Windows: `%APPDATA%\GitHub Copilot\settings.json` / Linux+Mac: `~/.config/github-copilot/settings.json` |

#### Parameters

| Param | Type | Required | Description |
|-------|------|----------|-------------|
| `action` | `string` | yes | `install-hooks` / `uninstall-hooks` / `status` |
| `client` | `string` | no | `claude` (default) or `copilot` |
| `dry_run` | `bool` | no | Default `false`. If `true`, returns diff without writing. |

---

## Data Model

New Postgres table, added to existing schema migration:

```sql
CREATE TABLE IF NOT EXISTS nebula_sync_state (
    file_path   TEXT        PRIMARY KEY,
    last_hash   TEXT        NOT NULL,
    synced_at   TIMESTAMPTZ NOT NULL DEFAULT now()
);
```

---

## Error Handling

- All three sync phases are wrapped in individual try/catch. Failures accumulate in `SyncSummary.Errors`, never propagate to MCP caller.
- Settings file writes use write-to-temp + atomic rename. Partial writes are impossible.
- If the auto-memory base directory does not exist, Phase 1 is skipped with a warning (not an error) — Claude Code may not have generated any memories yet.

---

## Testing

| Layer | Coverage |
|-------|----------|
| Unit | `AutoMemorySyncService` with mocked `IFileSystem` and mocked store |
| Unit | `HookInstallService` with mocked settings file reads/writes for both clients |
| Unit | `SyncSummary` serialization |
| Integration | Full sync cycle against test Postgres instance with real files |
| Integration | `nebula_setup install-hooks` → verify settings file mutation + idempotency |

---

## Implementation Order

1. `nebula_sync_state` migration
2. `AutoMemorySyncService` + unit tests
3. `memory(action: "sync")` MCP handler
4. `HookInstallService` + unit tests
5. `nebula_setup` MCP tool
6. Integration tests
7. Update `AGENTS.md` session protocol with sync action
8. Update `README.md`
