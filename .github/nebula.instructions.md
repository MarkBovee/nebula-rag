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
2. memory recall and persistence
3. planning and task progress
4. setup and diagnostics
5. later, session continuity and workflow orchestration

## Tool Routing

Prefer the consolidated Nebula MCP tools:

1. `rag_query`
2. `rag_ingest`
3. `rag_sources`
4. `rag_admin`
5. `memory`
6. `plan`
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

## Next Planned Capabilities

1. unified workflow actions such as batch query and fetch-and-index
2. a `doctor` command and diagnostic MCP action
3. session snapshots keyed by `sessionId` and `projectId`
4. lexical and exact-match fallback beneath semantic search