---
phase: 01-database-schema-domain-models
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/NebulaRAG.Core/Models/PlanModels.cs
  - src/NebulaRAG.Core/Storage/PostgresPlanStore.cs
autonomous: true
requirements:
  - PLAN-06
  - TASK-03
  - TASK-04
  - PERF-01
  - PERF-02
  - PERF-03
user_setup: []

must_haves:
  truths:
    - PostgreSQL tables exist for plans, tasks, plan_history, task_history
    - CHECK constraints enforce valid status values at database level for plans and tasks
    - Composite indexes exist on (session_id, status) and (project_id, name) for efficient queries
    - C# domain models (Plan, PlanTask, PlanStatus, TaskStatus) map cleanly to database schema
    - Cascade delete configured so deleting a plan automatically removes its tasks
  artifacts:
    - path: "src/NebulaRAG.Core/Models/PlanModels.cs"
      provides: "C# domain models and enums for plan lifecycle"
      min_lines: 100
    - path: "src/NebulaRAG.Core/Storage/PostgresPlanStore.cs"
      provides: "PostgreSQL schema initialization with constraints and indexes"
      min_lines: 150
  key_links:
    - from: "PostgresPlanStore.InitializeSchemaAsync()"
      to: "PlanModels.cs enums"
      via: "CHECK constraints use enum values directly"
---

# Plan 01: Database Schema & Domain Models

**Created:** 2026-02-27
**Phase:** 1 - Database Schema & Domain Models
**Status:** Draft
**Wave:** 1

## Overview

This plan establishes the foundation layer for the NebulaRAG+ Plan Lifecycle Management system by creating PostgreSQL tables and C# domain models. The database schema enforces status constraints at the database level, creates efficient indexes for queries, and configures cascade delete behavior. C# domain models map cleanly to the schema using records for DTOs and classes for aggregates.

## Success Criteria (from Roadmap)

1. PostgreSQL tables created for plans, tasks, plan_history, task_history with proper relationships
2. CHECK constraints enforce valid status values at database level for plans and tasks
3. Composite indexes exist on (session_id, status) and (project_id, name) for efficient queries
4. C# domain models (Plan, PlanTask, PlanStatus, TaskStatus) map cleanly to database schema
5. Cascade delete configured so deleting a plan automatically removes its tasks

## Dependencies

- None (this is the first phase in the roadmap)

## Tasks

<task type="auto">
  <name>Task 1: Create C# Domain Models</name>
  <files>src/NebulaRAG.Core/Models/PlanModels.cs</files>
  <action>Create domain models and enums following existing patterns from MemoryModels.cs.

1. **PlanStatus enum**: Draft, Active, Completed, Archived
   - Must match CHECK constraint values exactly
   - Add XML documentation for each value

2. **TaskStatus enum**: Pending, InProgress, Completed, Failed
   - Must match CHECK constraint values exactly
   - Add XML documentation for each value

3. **PlanRecord** (immutable DTO record):
   - Properties: Id (long), ProjectId (string), SessionId (string), Name (string), Description (string?), Status (PlanStatus), CreatedAt (DateTimeOffset), UpdatedAt (DateTimeOffset), Metadata (JsonDocument)
   - Add XML &lt;summary&gt; and &lt;param&gt; tags for all properties

4. **PlanTaskRecord** (immutable DTO record):
   - Properties: Id (long), PlanId (long), Title (string), Description (string?), Priority (string), Status (TaskStatus), CreatedAt (DateTimeOffset), UpdatedAt (DateTimeOffset), Metadata (JsonDocument)
   - Add XML &lt;summary&gt; and &lt;param&gt; tags for all properties

5. **PlanHistoryRecord** (immutable DTO record):
   - Properties: Id (long), PlanId (long), OldStatus (string?), NewStatus (string), ChangedBy (string), ChangedAt (DateTimeOffset), Reason (string?)
   - Add XML &lt;summary&gt; and &lt;param&gt; tags for all properties

6. **TaskHistoryRecord** (immutable DTO record):
   - Properties: Id (long), TaskId (long), OldStatus (string?), NewStatus (string), ChangedBy (string), ChangedAt (DateTimeOffset), Reason (string?)
   - Add XML &lt;summary&gt; and &lt;param&gt; tags for all properties

Follow existing patterns from MemoryModels.cs - use records with XML documentation, proper using directives (System.Text.Json for JsonDocument), and intention-revealing names. Avoid fully qualified type names - add appropriate using statements.</action>
  <verify>dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj</verify>
  <done>File exists at correct path, all enums and records defined with XML documentation, build completes with 0 errors 0 warnings, property names match database column names (PascalCase vs snake_case)</done>
</task>

<task type="auto">
  <name>Task 2: Create PostgreSQL Schema Initialization</name>
  <files>src/NebulaRAG.Core/Storage/PostgresPlanStore.cs</files>
  <action>Create a new PostgresPlanStore class following these specifications:

1. **Namespace**: NebulaRAG.Core.Storage

2. **Constructor**:
   - Accepts connectionString (string)
   - Validates that connectionString is not null or empty (throw ArgumentException if invalid)
   - Stores connection string in readonly field (same pattern as PostgresRagStore)

3. **InitializeSchemaAsync method**:
   - Public async method accepting CancellationToken
   - Returns Task
   - Creates all tables and indexes in a single SQL execution block using multi-line string interpolation
   - Follow the exact SQL structure from CONTEXT.md decisions

SQL to create:

CREATE TABLE IF NOT EXISTS plans (
    id BIGSERIAL PRIMARY KEY,
    project_id TEXT NOT NULL,
    session_id TEXT NOT NULL,
    name TEXT NOT NULL,
    description TEXT,
    status TEXT NOT NULL CHECK (status IN ('draft', 'active', 'completed', 'archived')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    metadata JSONB DEFAULT '{}'
);

CREATE TABLE IF NOT EXISTS tasks (
    id BIGSERIAL PRIMARY KEY,
    plan_id BIGINT NOT NULL REFERENCES plans(id) ON DELETE CASCADE,
    title TEXT NOT NULL,
    description TEXT,
    priority TEXT NOT NULL DEFAULT 'normal',
    status TEXT NOT NULL CHECK (status IN ('pending', 'in_progress', 'completed', 'failed')),
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    metadata JSONB DEFAULT '{}'
);

CREATE TABLE IF NOT EXISTS plan_history (
    id BIGSERIAL PRIMARY KEY,
    plan_id BIGINT NOT NULL REFERENCES plans(id) ON DELETE CASCADE,
    old_status TEXT,
    new_status TEXT,
    changed_by TEXT NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    reason TEXT
);

CREATE TABLE IF NOT EXISTS task_history (
    id BIGSERIAL PRIMARY KEY,
    task_id BIGINT NOT NULL REFERENCES tasks(id) ON DELETE CASCADE,
    old_status TEXT,
    new_status TEXT,
    changed_by TEXT NOT NULL,
    changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    reason TEXT
);

CREATE INDEX IF NOT EXISTS ix_plans_session_status ON plans(session_id, status);
CREATE INDEX IF NOT EXISTS ix_plans_project_name ON plans(project_id, name);
CREATE INDEX IF NOT EXISTS ix_tasks_plan_id ON tasks(plan_id);
CREATE INDEX IF NOT EXISTS ix_plan_history_plan_id ON plan_history(plan_id);
CREATE INDEX IF NOT EXISTS ix_task_history_task_id ON task_history(task_id);

4. **Connection handling**:
   - Use await using var connection = new NpgsqlConnection(_connectionString)
   - Use await using var command = new NpgsqlCommand(sql, connection)
   - Open connection with await connection.OpenAsync(cancellationToken)
   - Execute with await command.ExecuteNonQueryAsync(cancellationToken)

5. **XML Documentation**:
   - Add class-level &lt;summary&gt; explaining the class purpose
   - Add &lt;param&gt; and &lt;returns&gt; tags for InitializeSchemaAsync
   - Document that the method is idempotent (safe to run multiple times)

Follow the exact pattern from PostgresRagStore.InitializeSchemaAsync for consistency.</action>
  <verify>dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj</verify>
  <done>File exists at correct path, InitializeSchemaAsync method creates all four tables, all CHECK constraints present (plan status and task status), all five indexes present (two composite three single), foreign key ON DELETE CASCADE configured on tasks.plan_id plan_history.plan_id task_history.task_id, build completes with 0 errors 0 warnings, schema initialization follows same idempotent pattern as PostgresRagStore</done>
</task>

## Verification Criteria

After completing all tasks, verify:

1. [ ] PlanModels.cs exists and compiles without errors
2. [ ] PostgresPlanStore.cs exists and compiles without errors
3. [ ] All enum values match CHECK constraint values exactly
4. [ ] All foreign keys include ON DELETE CASCADE
5. [ ] All indexes use IF NOT EXISTS pattern
6. [ ] XML documentation is present on all public types and members
7. [ ] Schema initialization follows same pattern as PostgresRagStore
8. [ ] Build produces 0 errors and 0 warnings

## Testing Strategy

This phase focuses on schema and models. Testing will be deferred to Phase 2 (Storage Layer) where CRUD operations can be verified end-to-end. However, compilation success is verified as a quality gate.

## Notes

- Follow existing NebulaRAG patterns from PostgresRagStore for consistency
- Use Npgsql 10.0.1 (already referenced in NebulaRAG.Core.csproj)
- Use connection string pattern same as PostgresRagStore constructor
- Use async/await pattern with CancellationToken support
- Connection pooling handled by Npgsql (no explicit connection management needed)
- Table names use lowercase snake_case per CONTEXT.md decision
- C# property names use PascalCase matching database columns
