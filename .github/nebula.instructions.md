# Nebula Unified Instructions

This file is the canonical human-facing operating guide for NebulaRAG setup and agent usage.

Tool-specific files such as `AGENTS.md`, `.github/copilot-instructions.md`, and `.github/instructions/rag.instructions.md` remain in place for compatibility, but this file is the top-level Nebula guide to copy, read, and maintain first.

## Installation

Use `scripts/setup-nebula-rag.ps1` as the primary installation path.

The setup script is responsible for:

1. selecting the install target (`HomeAssistantAddon` or `LocalContainer`)
2. registering the Nebula MCP server for supported clients
3. scaffolding the Nebula instruction files into a project
4. copying the Nebula skill and environment template when required

## Canonical Operating Model

Use NebulaRAG as an MCP-first local stack for:

1. RAG retrieval
2. memory recall and persistence — with `short_term` and `long_term` tier support
3. setup and diagnostics
4. session continuity via auto-memory sync and review cycle

## Memory Tier Model

NebulaRAG memory supports two tiers:

- `short_term` (default) — session-scoped notes, transient context, in-progress decisions.
- `long_term` — durable architectural decisions, recurring bug patterns, project conventions, operational runbooks.

Pass `tier` on `store`, `recall`, and `update` actions. Use `action: "review"` + `subAction: "list"` to inspect auto-captured memories before promoting them to long-term.

## Tool Routing

Prefer the consolidated Nebula MCP tools:

1. `rag_query`
2. `rag_ingest`
3. `rag_sources`
4. `rag_admin`
5. `memory`
7. `system`

## Instruction Layout

The current compatible instruction set remains:

1. `AGENTS.md` for the repository operating model
2. `.github/copilot-instructions.md` for Copilot-specific behavior
3. `.github/instructions/rag.instructions.md` for RAG-first retrieval behavior
4. `.github/skills/nebularag/SKILL.md` for reusable Nebula workflow guidance

This file exists to keep those pieces aligned under one Nebula-first setup story.

## Relationship To Context-Mode

NebulaRAG should absorb the parts of context-mode that improve Nebula workflows, especially setup, workflow ergonomics, large-output handling, and session continuity.

NebulaRAG is not currently trying to become a full general-purpose sandbox execution server.

## Shipped Capabilities

1. Unified RAG + memory + admin MCP tools with project and session scoping
2. `nebula_setup` — `install-hooks`, `uninstall-hooks`, `status` with endpoint health check
3. Memory tiers: `short_term` / `long_term` with tier-scoped recall
4. Auto-memory sync via `action: "sync"` and review cycle via `action: "review"`
5. Lexical fallback search beneath semantic memory recall
6. Hybrid ranking for memory recall (semantic + lexical)