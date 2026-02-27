# Architecture Research

**Domain:** Plan/Task Lifecycle Management System
**Researched:** 2026-02-27
**Confidence:** MEDIUM

## Standard Architecture

### System Overview

```
┌─────────────────────────────────────────────────────────────┐
│                    Presentation Layer                      │
│  (MCP stdio / HTTP endpoints / CLI commands)            │
├─────────────────────────────────────────────────────────────┤
│  ┌──────────┐  ┌──────────┐  ┌──────────┐           │
│  │   MCP    │  │   HTTP   │  │   CLI    │           │
│  │ Tools    │  │  API     │  │ Commands │           │
│  └────┬─────┘  └────┬─────┘  └────┬─────┘           │
│       │            │            │                       │
├───────┴────────────┴────────────┴───────────────────────┤
│                    Application Layer                      │
│  ┌──────────────────────────────────────────────────┐    │
│  │        Plan Service (Orchestration)            │    │
│  │  - Create, Update, Archive plans              │    │
│  │  - Task status transitions                    │    │
│  │  - Active plan session enforcement            │    │
│  └──────────────────────────────────────────────────┘    │
│  ┌──────────────────────────────────────────────────┐    │
│  │       Task Service (Task Lifecycle)             │    │
│  │  - Add, Update, Complete tasks               │    │
│  │  - Task status validation                    │    │
│  └──────────────────────────────────────────────────┘    │
├─────────────────────────────────────────────────────────────┤
│                    Domain Layer                           │
│  ┌─────────────┐  ┌─────────────┐  ┌───────────────┐  │
│  │   Plan      │  │    Task     │  │  Domain       │  │
│  │  Aggregate  │  │  Aggregate  │  │  Exceptions   │  │
│  └─────┬───────┘  └─────┬───────┘  └───────────────┘  │
├────────┴──────────────────┴──────────────────┴───────────┤
│                  Infrastructure Layer                      │
│  ┌────────────────────────────────────────────────────┐    │
│  │      PostgreSQL Plan Store                      │    │
│  │  - plans table (parent)                        │    │
│  │  - tasks table (child, cascade delete)         │    │
│  │  - plan_history table (audit trail)             │    │
│  └────────────────────────────────────────────────────┘    │
└─────────────────────────────────────────────────────────────┘
```

### Component Responsibilities

| Component | Responsibility | Typical Implementation |
|-----------|----------------|------------------------|
| **PlanService** | Manages plan lifecycle (create, activate, complete, archive), enforces "one active per session" rule | Service class with dependency injection, orchestrates storage operations and business rules |
| **TaskService** | Manages individual task lifecycle (add, update, mark complete), validates status transitions | Service class coordinating task CRUD operations |
| **PostgresPlanStore** | Database operations for plans and tasks, handles transactions and queries | Repository/store pattern following existing `PostgresRagStore` style |
| **MCP Tools Handler** | Exposes plan operations as MCP tools (create_plan, get_plan, update_plan, etc.) | JSON-RPC method handlers integrated into `McpTransportHandler` |
| **Plan Aggregate** | Domain model encapsulating plan state and invariants | C# record/class with validation logic |
| **Task Aggregate** | Domain model for individual tasks with status enforcement | C# record/class with status enum |

## Recommended Project Structure

```
src/NebulaRAG.Core/
├── Models/
│   ├── PlanModels.cs          # Plan, PlanTask, PlanHistory, PlanStatus, TaskStatus enums
│   └── PlanStats.cs         # Plan analytics (completion rates, etc.)
├── Storage/
│   └── PostgresPlanStore.cs # Database operations for plans/tasks
├── Services/
│   ├── PlanService.cs        # Plan lifecycle orchestration
│   └── TaskService.cs       # Task lifecycle orchestration
└── Exceptions/
    └── PlanException.cs      # Base exception + specific types

src/NebulaRAG.Mcp/
└── McpTransportHandler.cs    # Add plan-related tool handlers

src/NebulaRAG.Cli/
└── Commands/
    └── PlanCommands.cs      # CLI command handlers for plan mgmt
```

### Structure Rationale

- **`Models/`**: Follows existing pattern where domain models live in `NebulaRAG.Core/Models/`. Using records for immutable DTOs, classes for aggregates with behavior.
- **`Storage/`**: Follows `PostgresRagStore` pattern. Single store class encapsulates all SQL, uses Npgsql, handles transactions.
- **`Services/`**: Follows existing service layer (`RagQueryService`, `RagManagementService`). Business logic lives here, not in storage layer.
- **`Exceptions/`**: Follows `RagException` hierarchy. Domain-specific exceptions for clear error handling.
- **`McpTransportHandler` extension**: Add new tool methods directly to existing handler following JSON-RPC pattern.
- **CLI Commands**: Separate command handlers following typical ASP.NET CLI patterns.

## Architectural Patterns

### Pattern 1: Service Layer with Repository/Store

**What:** Business logic encapsulated in service classes, data access delegated to store class. Services orchestrate multiple operations and enforce business rules.

**When to use:** Any application with domain logic that spans multiple data entities and requires transactional integrity.

**Trade-offs:**
- Pros: Clear separation of concerns, testable, follows existing NebulaRAG patterns
- Cons: Can create "anemic domain models" if all behavior moves to services

**Example:**
```csharp
// Service orchestrates business rules and delegates to store
public sealed class PlanService
{
    private readonly PostgresPlanStore _store;
    private readonly ILogger<PlanService> _logger;

    public async Task<Plan> CreatePlanAsync(
        string sessionId,
        string projectId,
        string name,
        IReadOnlyList<PlanTask> initialTasks,
        CancellationToken cancellationToken = default)
    {
        // Business rule: Only one active plan per session
        var existingActive = await _store.GetActivePlanAsync(sessionId, cancellationToken);
        if (existingActive is not null)
        {
            throw new PlanException("An active plan already exists for this session");
        }

        var plan = new Plan(
            SessionId: sessionId,
            ProjectId: projectId,
            Name: name,
            Status: PlanStatus.Draft,
            Tasks: initialTasks);

        return await _store.CreatePlanAsync(plan, cancellationToken);
    }

    public async Task CompleteTaskAsync(
        long planId,
        long taskId,
        CancellationToken cancellationToken = default)
    {
        // Business rule: Can't complete task in non-active plan
        var plan = await _store.GetPlanAsync(planId, cancellationToken)
            ?? throw new PlanException($"Plan {planId} not found");

        if (plan.Status != PlanStatus.Active)
        {
            throw new PlanException("Can only complete tasks in active plans");
        }

        await _store.UpdateTaskStatusAsync(taskId, TaskStatus.Completed, cancellationToken);
    }
}
```

### Pattern 2: Aggregate Root for State Validation

**What:** Domain model (Plan) encapsulates its invariants and validates state changes. Tasks are children managed through the aggregate.

**When to use:** When entities have clear parent-child relationships and business rules span both.

**Trade-offs:**
- Pros: Business rules close to data, prevents invalid states
- Cons: More complex model classes, need careful serialization for storage

**Example:**
```csharp
public sealed class Plan
{
    public long Id { get; }
    public string SessionId { get; }
    public string ProjectId { get; }
    public string Name { get; }
    public PlanStatus Status { get; private set; }
    public IReadOnlyList<PlanTask> Tasks { get; }

    public Plan Activate()
    {
        if (Status != PlanStatus.Draft)
        {
            throw new InvalidOperationException("Only draft plans can be activated");
        }
        Status = PlanStatus.Active;
        return this;
    }

    public Plan Complete()
    {
        if (Status != PlanStatus.Active)
        {
            throw new InvalidOperationException("Only active plans can be completed");
        }
        if (Tasks.Any(t => t.Status != TaskStatus.Completed))
        {
            throw new InvalidOperationException("All tasks must be completed before completing plan");
        }
        Status = PlanStatus.Completed;
        return this;
    }

    public Plan Archive()
    {
        if (Status == PlanStatus.Archived)
        {
            throw new InvalidOperationException("Plan is already archived");
        }
        Status = PlanStatus.Archived;
        return this;
    }
}
```

### Pattern 3: Enum with Valid State Transitions

**What:** Status fields are enums with explicit state machine enforcement. Invalid transitions throw exceptions.

**When to use:** Status-driven workflows with clear, defined transitions.

**Trade-offs:**
- Pros: Prevents invalid states at runtime, self-documenting
- Cons: Requires maintenance when adding states/transitions

**Example:**
```csharp
public enum PlanStatus
{
    Draft,
    Active,
    Completed,
    Archived
}

public enum TaskStatus
{
    Pending,
    InProgress,
    Completed,
    Failed
}

public static class PlanStatusTransitions
{
    private static readonly Dictionary<PlanStatus, PlanStatus[]> ValidTransitions = new()
    {
        [PlanStatus.Draft] = new[] { PlanStatus.Active, PlanStatus.Archived },
        [PlanStatus.Active] = new[] { PlanStatus.Completed, PlanStatus.Archived },
        [PlanStatus.Completed] = new[] { PlanStatus.Archived },
        [PlanStatus.Archived] = Array.Empty<PlanStatus>()
    };

    public static bool CanTransition(PlanStatus from, PlanStatus to)
    {
        return ValidTransitions.TryGetValue(from, out var allowed) && allowed.Contains(to);
    }
}

// Usage
if (!PlanStatusTransitions.CanTransition(currentStatus, newStatus))
{
    throw new PlanException($"Cannot transition plan from {currentStatus} to {newStatus}");
}
```

## Data Flow

### Request Flow

```
[Agent/AI] → [MCP Tool Call] → [PlanService] → [PostgresPlanStore] → [PostgreSQL]
                    ↓                    ↓                 ↓                ↓
[Response] ← [JSON-RPC Reply] ← [Domain Model] ← [SQL Result] ← [Data]
```

### Plan Creation Flow

```
1. Agent calls: mcp_create_plan(name="Implement feature", projectId="nebula-rag")
2. McpTransportHandler.HandleAsync routes to CreatePlan handler
3. PlanService.CreatePlanAsync validates (one active per session rule)
4. PostgresPlanStore.CreatePlanAsync opens transaction
5. INSERT INTO plans ... RETURNING id
6. INSERT INTO tasks (plan_id, title, status) VALUES ...
7. Transaction commits
8. PlanService returns created Plan model
9. McpTransportHandler serializes to JSON-RPC response
10. Agent receives plan with ID and initial tasks
```

### Task Completion Flow

```
1. Agent calls: mcp_complete_task(planId=123, taskId=456)
2. PlanService.CompleteTaskAsync validates (plan must be active)
3. PostgresPlanStore.UpdateTaskStatusAsync:
   - SELECT plan FROM plans WHERE id = 123 FOR UPDATE
   - UPDATE tasks SET status = 'completed', completed_at = NOW() WHERE id = 456
   - Check if all tasks completed → potentially auto-complete plan
4. Return updated task status
5. Agent continues to next task or marks plan complete
```

### Active Plan Enforcement Flow

```
1. Multiple agent sessions attempt to create/activate plans
2. Each PlanService.CreatePlanAsync / ActivatePlanAsync checks:
   SELECT id FROM plans WHERE session_id = @sid AND status = 'active'
3. If result exists → throw PlanException
4. PostgreSQL row-level locking (FOR UPDATE) prevents race condition
5. Agent receives error, must complete/archive existing plan first
```

### Key Data Flows

1. **Plan Creation:** Agent → MCP → Service → Store → Database (transactional inserts for plan + tasks)
2. **Task Updates:** Agent → MCP → Service → Store → Database (single update with status validation)
3. **Plan Lookup:** Agent → MCP → Service → Store → Database (read by projectId + name or by ID)
4. **Plan Archival:** Agent → MCP → Service → Store → Database (status update, optional cleanup job)

## Scaling Considerations

| Scale | Architecture Adjustments |
|-------|--------------------------|
| 0-1k agents | Single PostgreSQL instance, no changes needed. In-memory caching optional. |
| 1k-100k agents | Add connection pooling (Npgsql pooling), add indexes on (session_id, status) for active plan queries, consider read replicas for plan lookups. |
| 100k+ agents | Consider partitioning `plans` table by `session_id` or `created_at` time ranges, add Redis for caching active plan lookups, separate write/read databases. |

### Scaling Priorities

1. **First bottleneck:** Concurrent `active_plan` lookups per session. Add composite index on `(session_id, status)`. Use advisory locks or row-level locking for "one active per session" enforcement.
2. **Second bottleneck:** High-volume task updates. Batch task status updates, use upsert patterns for idempotent operations, consider separating task history to archive table.

## Anti-Patterns

### Anti-Pattern 1: No Transaction Boundaries

**What people do:** Insert plan, then insert tasks in separate calls without transaction. Network failure between calls leaves orphaned tasks or missing plan.

**Why it's wrong:** Data inconsistency - plan exists without tasks or tasks reference non-existent plan.

**Do this instead:** Wrap plan + tasks creation in a single PostgreSQL transaction. `PostgresPlanStore` should use `BeginTransactionAsync` / `CommitAsync`.

```csharp
// Wrong
var planId = await store.InsertPlanAsync(plan);
foreach (var task in tasks)
{
    await store.InsertTaskAsync(planId, task); // If this fails, we have orphan plan
}

// Correct
await using var connection = new NpgsqlConnection(_connectionString);
await connection.OpenAsync(cancellationToken);
await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

try
{
    var planId = await store.InsertPlanAsync(plan, connection, transaction);
    foreach (var task in tasks)
    {
        await store.InsertTaskAsync(planId, task, connection, transaction);
    }
    await transaction.CommitAsync(cancellationToken);
}
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

### Anti-Pattern 2: Status Violation in Database

**What people do:** Allow any status transition at database level. Application layer checks but database doesn't enforce.

**Why it's wrong:** Direct database access, migrations, or bugs can create invalid states. Data integrity depends solely on application code.

**Do this instead:** Add CHECK constraints to PostgreSQL schema for status enums and transitions.

```sql
-- Enforce only valid status values
CREATE TABLE plans (
    status TEXT NOT NULL CHECK (status IN ('draft', 'active', 'completed', 'archived'))
);

-- For complex transition rules, use triggers or keep in application layer
```

### Anti-Pattern 3: Over-Normalization

**What people do:** Separate `plans`, `tasks`, `plan_metadata`, `task_metadata`, `plan_tags`, `task_tags` into many small tables.

**Why it's wrong:** Unnecessary JOIN complexity for simple lookups. Plan/task data is typically read together, not queried separately.

**Do this instead:** Use JSONB columns for flexible metadata. Keep core fields as columns, optional metadata as JSONB.

```sql
CREATE TABLE plans (
    id BIGSERIAL PRIMARY KEY,
    session_id TEXT NOT NULL,
    project_id TEXT,
    name TEXT NOT NULL,
    status TEXT NOT NULL,
    created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    metadata JSONB DEFAULT '{}'  -- Flexible fields without new table
);
```

### Anti-Pattern 4: No Audit Trail

**What people do:** Update status directly. No record of when/why/how changes happened.

**Why it's wrong:** Impossible to debug issues, answer "who completed this task?", or recover from mistakes.

**Do this instead:** Maintain a `plan_history` or `task_history` table tracking all status changes.

```sql
CREATE TABLE plan_history (
    id BIGSERIAL PRIMARY KEY,
    plan_id BIGINT NOT NULL REFERENCES plans(id) ON DELETE CASCADE,
    old_status TEXT,
    new_status TEXT NOT NULL,
    changed_by TEXT,  -- session_id or user_id
    changed_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
    reason TEXT
);
-- Insert before status update
```

## Integration Points

### External Services

| Service | Integration Pattern | Notes |
|---------|---------------------|-------|
| **PostgreSQL** | Direct Npgsql connection with connection pooling | Use same database/credentials as existing RAG system. Connection pooling via Npgsql. |
| **MCP Protocol (stdio)** | JSON-RPC 2.0 via stdin/stdout | Add new tool methods to existing `McpTransportHandler`. Follow framing detection logic. |
| **MCP Protocol (HTTP)** | JSON-RPC 2.0 via HTTP endpoint | Already exposed via `NebulaRAG.AddonHost`. Add tool registration. |
| **CLI** | Command pattern with System.CommandLine or similar | Add `plan` subcommand group (create, list, show, complete, archive). |

### Internal Boundaries

| Boundary | Communication | Notes |
|----------|---------------|-------|
| **PlanService ↔ PostgresPlanStore** | Method calls, dependency injection | Store is infrastructure, Service is application layer. Store handles all SQL. |
| **PlanService ↔ TaskService** | Method calls (optional) | Could combine into single service for simplicity. Separate if task logic becomes complex. |
| **McpTransportHandler ↔ PlanService** | Method calls for each tool | Handler creates service instance via DI or passed in constructor. No direct DB access. |
| **CLI ↔ PlanService** | Method calls via DI container | CLI commands are thin wrappers around service calls. |

### Database Integration Pattern

Following existing `PostgresRagStore` pattern:

```csharp
// Store class takes connection string in constructor
public sealed class PostgresPlanStore
{
    private readonly string _connectionString;

    public PostgresPlanStore(string connectionString)
    {
        _connectionString = connectionString;
    }

    public async Task InitializeSchemaAsync(CancellationToken cancellationToken = default)
    {
        var sql = """
            CREATE TABLE IF NOT EXISTS plans (
                id BIGSERIAL PRIMARY KEY,
                session_id TEXT NOT NULL,
                project_id TEXT,
                name TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('draft', 'active', 'completed', 'archived')),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                updated_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                completed_at TIMESTAMPTZ
            );

            CREATE TABLE IF NOT EXISTS tasks (
                id BIGSERIAL PRIMARY KEY,
                plan_id BIGINT NOT NULL REFERENCES plans(id) ON DELETE CASCADE,
                title TEXT NOT NULL,
                status TEXT NOT NULL CHECK (status IN ('pending', 'in_progress', 'completed', 'failed')),
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                completed_at TIMESTAMPTZ
            );

            -- Indexes for common queries
            CREATE INDEX IF NOT EXISTS ix_plans_session_status ON plans(session_id, status);
            CREATE INDEX IF NOT EXISTS ix_plans_project_name ON plans(project_id, name);
            CREATE INDEX IF NOT EXISTS ix_tasks_plan_id ON tasks(plan_id);
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    // CRUD methods follow...
}
```

## Build Order Implications

Based on component dependencies:

1. **Phase 1: Domain Models and Database Schema** (`Models/`, `Storage/InitializeSchemaAsync`)
   - Defines the data model (Plan, PlanTask, PlanStatus, TaskStatus)
   - Creates PostgreSQL tables and indexes
   - No dependencies on other components

2. **Phase 2: Storage Layer** (`PostgresPlanStore`)
   - Implements CRUD operations for plans and tasks
   - Uses Npgsql for database access
   - Depends on Phase 1 (models and schema)

3. **Phase 3: Service Layer** (`PlanService`, `TaskService`)
   - Implements business logic and orchestration
   - Enforces "one active plan per session" rule
   - Depends on Phase 2 (storage) and Phase 1 (models)

4. **Phase 4: MCP Integration** (Add to `McpTransportHandler`)
   - Exposes plan operations as MCP tools
   - Routes JSON-RPC requests to service layer
   - Depends on Phase 3 (services)

5. **Phase 5: CLI Integration** (Add `PlanCommands`)
   - Adds command-line interface for plan management
   - Depends on Phase 3 (services)

## Sources

- [DigitalOcean - Using Leases to Manage Multi-Instance Environments](https://www.digitalocean.com/community/tutorials/manage-multi-instance-environments-using-leases) - Multi-instance coordination patterns
- [CSDN - SQL Task Water Table Structure](https://m.php.cn/faq/1879999.html) - Task flow tracking and audit trail patterns
- [CSDN - Workflow Engine Database Design](https://m.blog.csdn.net/Chen_Victor/article/details/60469007) - Workflow database schema considerations
- [CSDN - Simple Workflow Database Design](https://www.cnblogs.com/wangjiamin/p/13728857.html) - Workflow table relationships
- [CSDN - What is Workflow](https://www.cnblogs.com/imust2008/p/18811920) - Workflow state machine fundamentals
- [GitCode - Skyvern Database Design](https://m.blog.csdn.net/gitblog_00149/article/details/154556419) - PostgreSQL workflow table patterns with JSONB
- [CSDN - go-workflow Component](https://m.blog.csdn.net/csde12/article/details/125142854) - Lightweight workflow engine architecture

---

*Architecture research for: Plan/Task Lifecycle Management System*
*Researched: 2026-02-27*
