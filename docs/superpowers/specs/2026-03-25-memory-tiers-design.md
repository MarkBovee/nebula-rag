# Memory Tiers Design Spec

**Date:** 2026-03-25
**Status:** Approved
**Feature:** Short-term / Long-term memory tiers with auto-pruning and review cycle

---

## Overview

All memories currently live in a single flat pool with a single global `RetentionDays` prune setting
that only applies to `auto-memory` tagged entries. This design introduces two explicit tiers:

- **Short-term** — auto-pruned after `ShortTermRetentionDays` (default 30). Default tier for all new memories.
- **Long-term** — never auto-pruned. Subject to a periodic review cycle (`LongTermReviewIntervalDays`, default 90).

Both the AI agent (via MCP) and the human (via dashboard) can assign tiers, promote/demote between tiers,
and manage the long-term review queue.

---

## 1. Data Model

### Schema changes to `memories` table

| Column | Type | Default | Notes |
|--------|------|---------|-------|
| `tier` | `TEXT NOT NULL` | `'short_term'` | Values: `short_term` \| `long_term` |
| `last_reviewed_at_utc` | `TIMESTAMPTZ NULL` | `NULL` | NULL = never reviewed |

Review due date is computed at query time:
- If `last_reviewed_at_utc` is NULL → due at `created_at_utc + LongTermReviewIntervalDays`
- Otherwise → due at `last_reviewed_at_utc + LongTermReviewIntervalDays`

### Model changes

`MemoryRecord` gains two new fields:
```csharp
string Tier                          // "short_term" | "long_term"
DateTimeOffset? LastReviewedAtUtc    // null = never reviewed
```

New `MemoryReviewStatus` string enum returned in review queries:
- `current` — not yet due
- `due` — overdue by 0–30 days
- `overdue` — overdue by >30 days

---

## 2. MCP Tool Changes

All changes are within the existing unified `memory` tool.

### `memory store` — new optional parameter

| Parameter | Type | Default | Notes |
|-----------|------|---------|-------|
| `tier` | string | `"short_term"` | `"short_term"` or `"long_term"` |

The agent explicitly passes the tier when storing. If omitted, defaults to `short_term`.

**Invalid tier value:** If `tier` is present and not one of `short_term` | `long_term`, the tool returns an MCP error result: `"Invalid tier value. Use: short_term or long_term."`

### `memory update` — new optional parameter

| Parameter | Type | Notes |
|-----------|------|-------|
| `tier` | string | Promotes or demotes an existing memory between tiers |

When tier changes:
- Promoted (ST → LT): `last_reviewed_at_utc` is cleared (null) so it enters the review cycle fresh
- Demoted (LT → ST): `last_reviewed_at_utc` is cleared; memory becomes subject to age-based pruning

**`memory update` does NOT stamp `last_reviewed_at_utc`** — only `memory review confirm` and `memory review update` set that field. This keeps the review cycle exclusively under the `review` sub-actions.

**Invalid tier value:** Same error contract as `memory store` above.

### `memory recall` — query behaviour

- **Default (no `tier` filter):** returns memories from **both tiers** so agents always find promoted memories
- **`tier: "short_term"`:** restricts results to short-term only
- **`tier: "long_term"`:** restricts results to long-term only
- Tier is included in each result record so the agent is aware of what it retrieved

### New action: `memory review` *(in-scope for this iteration)*

The `memory review` sub-actions are explicitly in-scope. They give the agent a first-class way to surface and act on the long-term review queue — not just promote/demote, but confirm, update content, and delete during a session.

Sub-actions under the unified `memory` tool:

| Action | Description |
|--------|-------------|
| `memory review list` | Returns long-term memories where review is due or overdue, ordered by most overdue first. Includes `reviewStatus` field. |
| `memory review confirm` | Marks a memory as reviewed — sets `last_reviewed_at_utc` to now |
| `memory review update` | Updates content **and** sets `last_reviewed_at_utc` to now in a single call |
| `memory review delete` | Deletes a long-term memory that is no longer relevant |

**`memory review confirm` and `memory review update` are the only actions that stamp `last_reviewed_at_utc`.** Calling `memory update` (the general update action) does not affect the review timestamp.

### `memory sync` — pruning scope fix

Phase 2 pruning changes from tag-based (`auto-memory`) to tier-based:
- **Before:** `DELETE WHERE tag = 'auto-memory' AND created_at < cutoff`
- **After:** `DELETE WHERE tier = 'short_term' AND created_at < cutoff`

Long-term memories are never touched by auto-prune, regardless of age or tags.

---

## 3. Short-Term Pruning

`AutoMemorySyncService` Phase 2 is updated:

- Prune condition: `tier = 'short_term'` AND `created_at_utc < now - ShortTermRetentionDays`
- **`ShortTermRetentionDays = 0` disables pruning entirely** — same behaviour as the existing `RetentionDays = 0` escape hatch. Set to 0 to keep all short-term memories indefinitely.
- Tag-independent — any short-term memory is eligible regardless of its tags
- Auto-memory bridge (Phase 1) stores synced files as `tier: short_term` explicitly (same behaviour as today, now explicit)

---

## 4. Long-Term Review — Dashboard + Tier Promotion

### Memory tab changes

- **Tab badge** — red count badge showing number of overdue long-term memories
- **Review Queue sub-section** — appears when any long-term memories are due/overdue
  - Columns: content preview, created date, last reviewed (or "never"), days overdue, review status badge
  - Per-row actions: **Confirm**, **Edit + Confirm**, **Delete**, **Promote/Demote tier**
  - Bulk action: **Mark all as reviewed**

### Tier badge on memory list

Every memory row in the main list shows a small tier badge:
- `ST` (grey) — short-term
- `LT` (blue) — long-term

### Promote / Demote rules

| Direction | Tier change | `last_reviewed_at_utc` | Effect |
|-----------|-------------|------------------------|--------|
| Promote | ST → LT | Cleared (NULL) | Enters review cycle fresh; never auto-pruned |
| Demote | LT → ST | Cleared (NULL) | Subject to `ShortTermRetentionDays` pruning again |

Both dashboard (human) and MCP `memory update` (agent) can trigger promote/demote.

---

## 5. Configuration

`AutoMemorySettings` after this change:

```json
{
  "AutoMemory": {
    "BaseDirectory": "~/.claude/projects",
    "ShortTermRetentionDays": 30,
    "LongTermReviewIntervalDays": 90,
    "RetentionDays": null
  }
}
```

| Key | Type | Default | Notes |
|-----|------|---------|-------|
| `ShortTermRetentionDays` | int | 30 | Age cutoff for short-term auto-pruning. **Minimum: 1. Set to 0 to disable pruning.** Invalid values (negative) are clamped to 0 with a startup warning. |
| `LongTermReviewIntervalDays` | int | 90 | How often long-term memories need review. **Minimum: 1.** Invalid values (0 or negative) cause a startup validation error — the server refuses to start with an invalid review interval to prevent all long-term memories being immediately overdue. |
| `RetentionDays` | int? | null | **Deprecated alias** — maps to `ShortTermRetentionDays` on load; existing installs continue to work |

Both values are hot-reloadable.

---

## 6. Migration

The DB migration is additive:

```sql
ALTER TABLE memories ADD COLUMN tier TEXT NOT NULL DEFAULT 'short_term';
ALTER TABLE memories ADD COLUMN last_reviewed_at_utc TIMESTAMPTZ NULL;
CREATE INDEX idx_memories_tier ON memories(tier);
CREATE INDEX idx_memories_review ON memories(tier, last_reviewed_at_utc) WHERE tier = 'long_term';
```

All existing memories default to `short_term` — no data is lost or changed in behaviour.

---

## 7. Out of Scope

- Automatic tier promotion based on AI scoring or access frequency (YAGNI — explicit tier assignment is sufficient)
- Separate tables per tier (unnecessary complexity)
- Review notifications / webhooks (can be added later if needed)
