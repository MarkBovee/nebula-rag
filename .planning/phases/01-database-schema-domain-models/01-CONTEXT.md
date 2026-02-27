# Phase 1: Database Schema & Domain Models - Context

**Gathered:** 2026-02-27
**Status:** Ready for planning

<domain>
## Phase Boundary

Foundation layer enabling plan and task persistence with database-enforced constraints. PostgreSQL tables (plans, tasks, plan_history, task_history) with CHECK constraints, composite indexes, and C# domain models that map cleanly to database schema.
</domain>

<decisions>
## Implementation Decisions

### Table Naming Conventions
- PostgreSQL table names: Lowercase snake_case (plans, tasks, plan_history, task_history)
- C# class names: PascalCase singular (Plan, PlanTask, PlanHistory)
- Consistency with existing NebulaRAG patterns (chunks, memories, etc.)

### Column Types & Data Types
- Primary keys: BIGSERIAL for auto-incrementing IDs
- Foreign keys: BIGINT referencing parent table IDs
- Session/project identifiers: TEXT NOT NULL (variable length strings)
- Names/titles: TEXT NOT NULL (unlimited text fields)
- Status fields: TEXT NOT NULL with CHECK constraints
- Timestamps: TIMESTAMPTZ NOT NULL DEFAULT NOW() for UTC storage
- Optional metadata: JSONB DEFAULT '{}' for flexible fields

### Constraint Patterns
- CHECK constraints on status columns to enforce valid enum values: IN ('draft', 'active', 'completed', 'archived') and IN ('pending', 'in_progress', 'completed', 'failed')
- Foreign key constraints: tasks.plan_id REFERENCES plans(id) ON DELETE CASCADE
- Cascade delete: When plan deleted, all tasks auto-delete
- NOT NULL constraints: Required fields enforced at database level

### Index Strategy
- Composite index: ix_plans_session_status ON plans(session_id, status) — for "one active per session" queries
- Composite index: ix_plans_project_name ON plans(project_id, name) — for plan lookup by projectId+name
- Single index: ix_tasks_plan_id ON tasks(plan_id) — for task list queries
- History indexes: ix_plan_history_plan_id, ix_task_history_task_id for audit queries

### Status Enum Values
- PlanStatus: 'draft', 'active', 'completed', 'archived'
- TaskStatus: 'pending', 'in_progress', 'completed', 'failed'
- Matched in CHECK constraints, used in C# enum definitions

### History Table Design
- plan_history: id (BIGSERIAL), plan_id (BIGINT FK), old_status (TEXT), new_status (TEXT), changed_by (TEXT), changed_at (TIMESTAMPTZ), reason (TEXT)
- task_history: id (BIGSERIAL), task_id (BIGINT FK), old_status (TEXT), new_status (TEXT), changed_by (TEXT), changed_at (TIMESTAMPTZ), reason (TEXT)
- Both history tables CASCADE delete when parent plan/task deleted

### C# Model Structure
- Use C# records for immutable DTOs (PlanRecord, TaskRecord)
- Use classes for aggregates with behavior (Plan, PlanTask)
- Properties match database column names with PascalCase convention
- Navigation properties for relationships (Tasks, History)

### Migration Strategy
- Initialize schema via PostgresPlanStore.InitializeSchemaAsync()
- Use CREATE TABLE IF NOT EXISTS to avoid errors on re-run
- Indexes created alongside tables in same initialization
- Idempotent: safe to run multiple times

</decisions>

<specifics>
## Specific Ideas

- Follow existing NebulaRAG conventions from PostgresRagStore (connection string pattern, Dapper usage)
- Use Npgsql 10.0.1 parameter binding for all SQL queries (no string concatenation)
- All operations use async/await pattern with CancellationToken support
- Connection pooling handled by Npgsql (no explicit connection management)

</specifics>

<deferred>
## Deferred Ideas

None — discussion stayed within phase scope.
</deferred>

---
*Phase: 01-database-schema-domain-models*
*Context gathered: 2026-02-27*
