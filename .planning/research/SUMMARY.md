# Project Research Summary

**Project:** NebulaRAG+ Plan Lifecycle Management
**Domain:** Plan/Task Lifecycle Management for AI Agents
**Researched:** 2026-02-27
**Confidence:** MEDIUM

## Executive Summary

This project adds plan lifecycle management to an existing .NET 10 + PostgreSQL RAG system. Agents will create execution plans with tasks, track progress through status updates, and archive completed plans. The system must support CRUD operations, enforce "one active plan per session" rule, and provide MCP endpoints for agent integration.

Recommended approach follows NebulaRAG's existing Clean Architecture pattern: Domain models in Core layer, storage layer using PostgreSQL with Dapper, service layer for business logic orchestration, and MCP handlers for tool exposure. Key risks include race conditions on active plan enforcement, partial transaction failures creating orphaned data, and missing audit trails for debugging and recovery.

## Key Findings

### Recommended Stack

Leverage existing NebulaRAG stack (.NET 10, PostgreSQL 16, Npgsql 10.0.1, Dapper 2.1.35) — no new infrastructure needed. Add plan storage as new tables in same PostgreSQL instance, use existing connection pooling and configuration patterns.

**Core technologies:**
- .NET 10.0 — Already in use, no migration needed
- PostgreSQL 16+ — Already in use, supports CHECK constraints and indexes
- Npgsql 10.0.1 — Already in use, supports advanced PostgreSQL features
- Dapper 2.1.35 — Lightweight ORM matching existing patterns

### Expected Features

**Must have (table stakes):**
- Create plan with initial tasks — Core CRUD operation
- Mark tasks as complete — Progress tracking
- Query plans by projectId + name — Agent lookup pattern
- Archive plans — Completion lifecycle
- One active plan per session — Session scoping requirement

**Should have (competitive):**
- None identified — Leverage existing NebulaRAG architecture as primary differentiator

**Defer (v2+):**
- Cleanup function for archived plans — Maintenance tool, depends on archiving mechanism
- Plan history query capabilities — Debugging support
- Task reordering — UI convenience

### Architecture Approach

Follow existing Clean Architecture with layered separation: Presentation (MCP/HTTP/CLI) → Application (PlanService, TaskService) → Domain (Plan, Task aggregates) → Infrastructure (PostgresPlanStore, PostgreSQL tables). Parent-child relationship between plans and tasks with cascade delete.

**Major components:**
1. PostgresPlanStore — Database operations for plans, tasks, and history with transactional integrity
2. PlanService — Business logic for plan lifecycle, active plan enforcement
3. TaskService — Task lifecycle management, status validation
4. MCP Handlers — JSON-RPC tool exposure for agent integration
5. Domain Models — Plan, PlanTask, status enums with transition validation

### Critical Pitfalls

1. **State machine without enforcement** — Application-layer validation alone allows invalid states over time. Prevention: Add CHECK constraints to PostgreSQL schema for status enums and history tables in Phase 1.

2. **Race condition on "one active plan" rule** — Concurrent check-then-act pattern can create duplicate active plans. Prevention: Use PostgreSQL SELECT FOR UPDATE or advisory locks in Phase 2.

3. **Partial transaction failure orphaning tasks** — Plan created without tasks if network fails between operations. Prevention: Wrap plan + tasks creation in single transaction in Phase 2.

4. **Scattered status transition logic** — Different code paths implement rules differently, creating inconsistency. Prevention: Central transition validator class used everywhere in Phase 1.

5. **No audit trail** — No record of who changed what and why, making debugging impossible. Prevention: Create plan_history and task_history tables in Phase 1, insert before every status change.

## Implications for Roadmap

Based on research, suggested phase structure:

### Phase 1: Database Schema & Domain Models
**Rationale:** Foundation layer with no dependencies. Tables, constraints, indexes, and domain models must exist before storage/service layers can function.
**Delivers:** PostgreSQL tables (plans, tasks, plan_history, task_history) with CHECK constraints, composite indexes, and C# domain models (Plan, PlanTask, status enums).
**Addresses:** Data storage foundation, status validation constraints, audit trail infrastructure
**Avoids:** Pitfalls 1, 5 (state enforcement, audit trail)

### Phase 2: Storage Layer
**Rationale:** Data access layer depends on schema and models. Wraps PostgreSQL operations with transaction handling and proper locking.
**Delivers:** PostgresPlanStore with CRUD operations, transaction boundaries, and locking patterns for concurrent operations.
**Uses:** Npgsql 10.0.1, Dapper 2.1.35, PostgreSQL 16 CHECK constraints
**Implements:** Infrastructure layer, atomic operations, SELECT FOR UPDATE locking

### Phase 3: Service Layer
**Rationale:** Business logic orchestration layer depends on storage. Enforces application rules and coordinates multi-step operations.
**Delivers:** PlanService (lifecycle, active plan enforcement) and TaskService (task management, status validation).
**Implements:** "One active plan per session" rule, centralized status transition validation

### Phase 4: MCP Integration
**Rationale:** Primary delivery target — agents interact via MCP. Thin adapter layer routing JSON-RPC to services.
**Delivers:** MCP tool handlers (create_plan, get_plan, update_plan, complete_task, archive_plan) integrated into existing McpTransportHandler.
**Implements:** Session authorization, error handling, plan exposure to agents

### Phase 5: Cleanup Function
**Rationale:** Post-MVP maintenance feature. Depends on archival mechanism existing from earlier phases.
**Delivers:** CLI/MCP tool for removing archived plans older than configured retention period.
**Implements:** Configurable retention, safe deletion (cascade to history)

### Phase Ordering Rationale

- **Dependencies drive order:** Schema → Storage → Services → Presentation follows clear dependency chain. Each phase can be tested incrementally.
- **Pitfall avoidance:** Phase 1 addresses status validation and audit trails (prevents data corruption). Phase 2 addresses race conditions and transaction integrity. Phases 3-4 leverage solid foundation.
- **Delivery focus:** MCP integration (Phase 4) is primary target since agents communicate via MCP. CLI (Phase 5) is convenience tooling.

### Research Flags

Phases likely needing deeper research during planning:
- **Phase 2:** Locking strategy verification — SELECT FOR UPDATE vs advisory locks vs partial unique index needs concurrency testing

Phases with standard patterns (skip research-phase):
- **Phase 1:** Well-documented PostgreSQL CHECK constraint pattern
- **Phase 3:** Service layer orchestration is established pattern in existing NebulaRAG codebase
- **Phase 4:** MCP tool registration follows existing pattern in McpTransportHandler

## Confidence Assessment

| Area | Confidence | Notes |
|------|------------|-------|
| Stack | MEDIUM | Reuses existing NebulaRAG stack, verified against codebase |
| Features | MEDIUM | Derived from user requirements, standard plan/task patterns |
| Architecture | MEDIUM | Aligns with existing Clean Architecture, standard for this domain |
| Pitfalls | MEDIUM | Based on established database patterns and common issues |

**Overall confidence:** MEDIUM

### Gaps to Address

- **Cleanup retention policy:** What's the retention period for archived plans? Configurable or fixed? Need to define during Phase 1 or 5.
- **Multi-instance coordination:** If multiple MCP server instances run, single-instance locking fails. Consider during Phase 2 or scale to 100k+ agents.
- **Task list validation:** Maximum task count per plan? Consider during Phase 3 service layer to prevent unwieldy plans from LLM over-generation.

## Sources

### Primary (HIGH confidence)
- Existing NebulaRAG codebase — Verified patterns for PostgresRagStore, service layer, MCP handlers
- PostgreSQL 16 documentation — CHECK constraints, partial indexes, transaction handling
- Project context from user interviews — Requirements definition

### Secondary (MEDIUM confidence)
- Common workflow engine patterns — State machine design, audit trail patterns
- Database design best practices — Parent-child relationships, cascade delete

---
*Research completed: 2026-02-27*
*Ready for roadmap: yes*
