# NebulaRAG+

## What This Is

A plan lifecycle management system for AI agents, built on top of NebulaRAG's existing RAG and memory capabilities. Agents can create plans with tasks, track progress through status updates, and archive completed plans. Plans are persisted in PostgreSQL and accessible via MCP endpoints for agent integration.

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
- [ ] Agent can retrieve a plan by projectId + name
- [ ] Agent can retrieve a plan by plan ID
- [ ] Agent can update plan details (name, description)
- [ ] Agent can archive a plan when complete
- [ ] Plan has a status (Draft, Active, Completed, Archived)
- [ ] Agent can mark a task as complete
- [ ] Agent can update task details (title, description, priority)
- [ ] Task has a status (Pending, InProgress, Completed, Failed)
- [ ] Tasks cascade delete when parent plan is deleted

### Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Real-time multi-agent collaboration | Single agent per session — simplified implementation |
| Plan templates | Agent generates fresh plans per request |
| Plan sharing between projects | Plans are scoped to projectId |
| External integrations (Notion, Jira) | PostgreSQL-only storage |

## Constraints

- **Tech stack**: Must use existing .NET 10 / PostgreSQL stack
- **Storage**: Plans stored in same PostgreSQL database (new tables)
- **Transport**: Plan operations exposed via MCP (stdio and HTTP)
- **Session scope**: Simplified — no active plan enforcement

## Key Decisions

| Decision | Rationale | Outcome |
|----------|-----------|---------|
| Removed "one active plan per session" enforcement | Simplifies Service Layer implementation, removes PLAN-07 from requirements | — Pending |

## Context

## Last updated: 2026-02-27 after removing PLAN-07

---

*Last updated: 2026-02-27 after removing PLAN-07*
