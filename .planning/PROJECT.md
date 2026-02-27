# NebulaRAG+

## What This Is

A plan lifecycle management system for AI agents, built on top of NebulaRAG's existing RAG and memory capabilities. Agents can create plans with tasks, track progress, update status during execution, and archive completed plans. Plans are persisted in PostgreSQL and accessible via MCP endpoints.

## Core Value

AI agents can reliably create, track, and complete execution plans with full persistence and retrieval.

## Requirements

### Validated

<!-- Shipped and confirmed valuable. -->

- ✓ RAG query and search — existing
- ✓ Memory storage and retrieval — existing
- ✓ PostgreSQL + pgvector storage — existing
- ✓ MCP (stdio and HTTP) endpoints — existing
- ✓ ASP.NET Core web API — existing
- ✓ CLI tools for RAG operations — existing

### Active

<!-- Current scope. Building toward these. -->

- [ ] Agent can create a plan (with name, projectId, initial tasks)
- [ ] Plan has a status (Draft, Active, Completed, Archived)
- [ ] Plan contains a list of tasks (each with status)
- [ ] Agent can mark tasks as complete
- [ ] Agent can update plan and task details as needed
- [ ] One active plan per session, but multiple plans can exist per project
- [ ] Agent can look up plans by projectId + name
- [ ] Plans can be archived when complete
- [ ] Archived plans persist (not immediately deleted)
- [ ] Cleanup function to remove archived plans
- [ ] MCP tools for plan CRUD operations
- [ ] CLI commands for plan management

### Out of Scope

<!-- Explicit boundaries. Includes reasoning to prevent re-adding. -->

- Real-time plan collaboration — Single agent per session, one active plan
- Plan sharing between agents — Plans are scoped to projectId
- Plan templates — Each plan is generated fresh by agents
- External integrations (Notion, Jira, etc.) — Postgres only

## Context

NebulaRAG already provides RAG query/search and persistent memory through PostgreSQL + pgvector. The new plan storage extends this to support execution plan lifecycle management. Agents will create plans when users ask them to work on something, track tasks as they complete them, and archive when done.

## Constraints

- **Tech stack**: Must use existing .NET 10 / PostgreSQL stack
- **Storage**: Plans stored in same PostgreSQL database (new tables)
- **Transport**: Plan operations exposed via MCP (stdio and HTTP)
- **Session scope**: One active plan per session enforced at runtime

## Key Decisions

<!-- Decisions that constrain future work. Add throughout project lifecycle. -->

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| PostgreSQL storage for plans | Leverages existing infrastructure, consistent with RAG/memory | — Pending |

---
*Last updated: 2026-02-27 after initialization*
