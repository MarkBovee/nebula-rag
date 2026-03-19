# NebulaRAG Roadmap

This roadmap captures the first NebulaRAG evolution path inspired by context-mode.

The goal is not to clone context-mode. The goal is to make NebulaRAG the default MCP, RAG, memory, planning, and session-continuity stack for Nebula-centric workflows.

## Direction

NebulaRAG should absorb the highest-value ideas from context-mode:

- scripted MCP installation
- one coherent Nebula instruction bundle
- higher-level workflow tools
- large-output handling
- session continuity
- better retrieval fallback behavior

NebulaRAG should not expand into general sandbox execution unless that becomes an explicit product goal.

## Positioning

Near term, NebulaRAG should replace context-mode for Nebula-focused workflows.

It does not need to replace context-mode's broader sandbox-runtime role to deliver value here.

## Phase 1: Setup And Operating Model

1. Standardize `scripts/setup-nebula-rag.ps1` as the canonical MCP installation and registration path.
2. Add one canonical Nebula instruction bundle and let the setup script scaffold client-compatible instruction files.
3. Rework setup documentation into one Nebula-first install and usage path.

## Phase 2: Workflow Ergonomics

1. Add compound workflow operations such as batch query, fetch-and-index, and index-then-query.
2. Add a `doctor` capability in MCP and CLI for database, schema, provider, install-target, and source health.
3. Add large-output handling so fetched and indexed content returns targeted excerpts and retrieval handles instead of raw payloads.
4. Add lazy provider adapters for embedding and fetch backends.

## Phase 3: Session And Retrieval Quality

1. Add session snapshots backed by PostgreSQL and scoped by `sessionId` and `projectId`.
2. Add lexical and exact-match retrieval fallback below semantic search.
3. Add tool and session telemetry suitable for debugging real MCP sessions.

## Phase 4: Hardening

1. Add deny-list security controls for ingestion paths and URL sources.
2. Add docs, validation coverage, and release-gate work for the new install and workflow surface.

## First Implementation Slice

This repository change starts Phase 1 by:

1. adding this roadmap
2. introducing a canonical Nebula instruction bundle
3. wiring the setup script to scaffold that bundle
4. updating the README to reflect the unified setup story

## Non-Goals For This Slice

1. No polyglot sandbox executor.
2. No hook-routing parity with context-mode.
3. No full session-snapshot implementation yet.