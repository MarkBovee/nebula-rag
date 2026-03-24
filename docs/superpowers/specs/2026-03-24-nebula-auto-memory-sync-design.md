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
| `nebula_setup` | New top-level tool with `install-hooks`, `uninstall-hooks`, `status` actions — handler at `NebulaRAG.Mcp/Tools/NebulaSetupToolHandler.cs` |

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
4. New or changed files → delete any existing RAG chunks for `source = auto-memory:{project-slug}`, then ingest via `RagIndexer` with source tag `auto-memory:{project-slug}` where `project-slug` is the parent directory name. Deleting before re-ingest prevents stale chunk accumulation if a file is moved or renamed.
5. Update `nebula_sync_state` with new hash and timestamp.
6. Unchanged files → skip.

#### Phase 2: Stale Memory Pruning

1. Phase 1 updates `nebula_sync_state.synced_at` for every file seen on disk during this run.
2. Query Nebula memory entries tagged `auto-memory` where `synced_at < NOW() - RetentionDays`. The link between memory entries and `nebula_sync_state` is the source tag `auto-memory:{project-slug}` — Phase 1 stamps `synced_at` in `nebula_sync_state` per file, and Phase 2 uses that timestamp to identify memory entries whose backing file has not been seen on disk within the retention window. A memory entry is considered stale when no file with its slug has been synced recently.
3. Delete matching entries via existing memory store.
4. `RetentionDays` configurable via `ragsettings.json` → `AutoMemory.RetentionDays` (default: `30`). Setting `0` disables pruning entirely.

#### Phase 3: Dirty RAG Source Reindex

1. Query all tracked sources from `RagSourcesManifestService`. The manifest already stores a SHA-256 `ContentHash` field per source (same format as Phase 1). No extension needed.
2. For each source backed by a file path: hash current content on disk using SHA-256.
3. Compare against `ContentHash` stored in manifest.
4. Dirty sources (hash mismatch) → delete existing chunks for that source, then re-chunk and re-embed via `RagIndexer`. Deletion before re-ingest prevents stale chunk accumulation even if the source slug or path changes.
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
- For each supported client: check if settings file exists, whether Nebula hook is present, whether the Nebula MCP HTTP endpoint is reachable (via `HTTP GET /health` with a 2-second timeout; failure is reported as a warning, not a hard error — the server may be the one running this check).
- Returns `HookStatusResult[]`, one entry per client.

#### Supported Clients

| Client | Settings Path |
|--------|--------------|
| `claude` | `~/.claude/settings.json` |
| `copilot` | Windows: `%APPDATA%\GitHub Copilot\settings.json` / Linux+Mac: `~/.config/github-copilot/settings.json` |

#### Parameters

| Param | Type | Required | Description |
|-------|------|----------|-------------|
| `action` | `string` | yes | `install-hooks` / `uninstall-hooks` / `status` |
| `client` | `string` | no | `claude` (default) or `copilot`. Applies to `install-hooks` and `uninstall-hooks` only. `status` always checks all supported clients and ignores this parameter. |
| `dry_run` | `bool` | no | Default `false`. If `true`, returns diff without writing. Applies to `install-hooks` and `uninstall-hooks` only. |

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

### Return Models

```csharp
// nebula_setup tool responses
public record HookOperationResult(
    bool Success,
    string Client,
    string? Diff,       // populated when dry_run: true
    string Message
);

public record HookStatusResult(
    string Client,
    bool SettingsFileExists,
    bool HookInstalled,
    bool EndpointReachable,
    string? EndpointWarning   // set if reachability check failed (non-blocking)
);
```

`AutoMemory.BaseDirectory` tilde (`~`) is resolved by the service itself via `Environment.GetFolderPath(Environment.SpecialFolder.UserProfile)` — not delegated to the shell.

---

## Error Handling

- All three sync phases are wrapped in individual try/catch. Failures accumulate in `SyncSummary.Errors`, never propagate to MCP caller.
- Settings file writes use write-to-temp + atomic rename. Partial writes are impossible.
- If the auto-memory base directory does not exist, Phase 1 is skipped with a warning (not an error) — Claude Code may not have generated any memories yet.

---

## Testing

| Layer | Coverage |
|-------|----------|
| Unit | `AutoMemorySyncService` Phase 1 — new file ingested, unchanged file skipped, unreadable file → error in `SyncSummary.Errors`, remaining files continue |
| Unit | `AutoMemorySyncService` Phase 2 — entries pruned when `synced_at` outside window; `RetentionDays: 0` skips pruning entirely |
| Unit | `AutoMemorySyncService` Phase 3 — dirty source reindexed, clean source skipped, missing file → warning in errors, source not deleted |
| Unit | `HookInstallService` — install writes hook, install is idempotent, uninstall removes hook, `dry_run: true` returns diff without writing |
| Unit | `HookInstallService` — both `claude` and `copilot` client paths resolved correctly per OS |
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
