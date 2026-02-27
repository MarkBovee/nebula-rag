# Pitfalls Research

**Domain:** Plan/Task Lifecycle Management System
**Researched:** 2026-02-27
**Confidence:** MEDIUM

## Critical Pitfalls

### Pitfall 1: State Machine Without Enforcement

**What goes wrong:**
Application code checks status transitions but the database allows any status value. Bugs, direct DB access, or migration scripts create invalid states (e.g., archived task in draft plan, completed plan with pending tasks). The system eventually breaks because code assumptions about valid states no longer hold.

**Why it happens:**
Developers implement validation in service layer but neglect database-level constraints. CHECK constraints are seen as "extra work" or "overkill" since the application handles it. Over time, other code paths, scripts, or bugs bypass the service layer.

**How to avoid:**
Define status values as CHECK constraints in PostgreSQL schema. For complex transition rules, either:
1. Use PostgreSQL triggers to enforce transition rules at DB level
2. Create a single canonical validation function called from both service layer and any DB entry points

**Warning signs:**
- SQL queries with `WHERE status = 'some_value'` without CHECK constraint on that column
- Multiple code paths updating status without going through a central service method
- Manual status fixes being documented in wikis/notes (indicates system allowed invalid state)

**Phase to address:**
Phase 1 (Database Schema) - Define CHECK constraints alongside table creation. This prevents invalid states from ever being stored.

---

### Pitfall 2: Race Condition on "One Active Plan" Rule

**What goes wrong:**
Multiple concurrent requests check for existing active plan, find none, then both proceed to create/activate a plan. Two "active" plans end up in the database, violating the "one active per session" rule. Downstream operations assume single active plan and behave unpredictably.

**Why it happens:**
The check-then-act pattern is performed without proper locking:
```csharp
// NOT SAFE - race condition
var existing = await store.GetActivePlanAsync(sessionId);
if (existing is null)
{
    // Another request might slip in here
    await store.CreateActivePlanAsync(sessionId, name);
}
```

**How to avoid:**
Use PostgreSQL advisory locks or SELECT FOR UPDATE to serialize the check-and-create operation. Either:
1. Use `SELECT ... FROM plans WHERE session_id = $1 AND status = 'active' FOR UPDATE` before insert
2. Use `pg_advisory_xact_lock()` on a session-specific key
3. Add a unique index on `(session_id)` WHERE status = 'active' (requires partial index)

**Warning signs:**
- Seeing duplicate active plans in production (even rarely)
- Timeouts increasing under concurrent load
- "I swear I saw this work yesterday" type bugs that only appear occasionally

**Phase to address:**
Phase 2 (Storage Layer) - Implement proper locking patterns in `PostgresPlanStore`. Test with concurrent requests to verify serialization.

---

### Pitfall 3: Partial Transaction Failure Orphaning Tasks

**What goes wrong:**
Plan is created, then tasks are inserted in separate calls without transaction. Network failure, crash, or exception between plan insertion and task inserts leaves a plan with zero tasks. UI shows plan exists but tasks list is empty. Querying tasks for this plan returns nothing.

**Why it happens:**
Developers separate concerns too early - create plan endpoint returns plan ID, then tasks are added in follow-up calls. Or they forget to wrap multi-step operations in a transaction. The "happy path" works fine but error paths create inconsistent state.

**How to avoid:**
Wrap plan + tasks creation in a single PostgreSQL transaction. The `PostgresPlanStore.CreatePlanAsync` method should accept initial tasks and insert everything atomically:
```csharp
await using var transaction = await connection.BeginTransactionAsync(cancellationToken);
try
{
    var planId = await InsertPlanAsync(plan, connection, transaction);
    foreach (var task in initialTasks)
    {
        await InsertTaskAsync(planId, task, connection, transaction);
    }
    await transaction.CommitAsync(cancellationToken);
}
catch
{
    await transaction.RollbackAsync(cancellationToken);
    throw;
}
```

**Warning signs:**
- Empty task lists on newly created plans
- Error recovery scripts that "fix orphaned plans"
- Separate API endpoints for creating plan vs adding tasks (suggests non-atomic design)

**Phase to address:**
Phase 2 (Storage Layer) - Ensure all multi-step database operations use transactions. Phase 3 (Service Layer) should never call multiple store methods without transaction coordination.

---

### Pitfall 4: Status Transition Logic Scattered Across Code

**What goes wrong:**
Different parts of the codebase implement status transition rules differently. One service allows `Draft → Archived`, another requires `Draft → Active → Archived`. Over time, developers add special cases ("just this once") creating inconsistent behavior. Maintenance becomes impossible because there's no single source of truth.

**Why it happens:**
Initial implementation puts validation in service layer. New features are added quickly, developers copy-paste validation or add inline checks. No central "transition validator" exists. Status enum is considered "just data" rather than "a state machine."

**How to avoid:**
Create a single canonical transition validator used everywhere:
```csharp
public static class PlanStatusTransitions
{
    private static readonly Dictionary<PlanStatus, PlanStatus[]> ValidTransitions = new()
    {
        [PlanStatus.Draft] = new[] { PlanStatus.Active, PlanStatus.Archived },
        [PlanStatus.Active] = new[] { PlanStatus.Completed, PlanStatus.Archived },
        [PlanStatus.Completed] = new[] { PlanStatus.Archived },
        [PlanStatus.Archived] = Array.Empty<PlanStatus>()
    };

    public static void ValidateTransition(PlanStatus from, PlanStatus to)
    {
        if (!ValidTransitions.TryGetValue(from, out var allowed) || !allowed.Contains(to))
        {
            throw new PlanException($"Cannot transition from {from} to {to}");
        }
    }
}
```

**Warning signs:**
- Searching for `PlanStatus.` returns 50+ occurrences across 20 files
- Finding `if (status == Status.X)` scattered in many services/controllers
- Documentation that says "status behavior may vary by entry point"

**Phase to address:**
Phase 1 (Domain Models) - Define transition validators alongside status enums. Make them impossible to bypass by requiring their use in all status-changing operations.

---

### Pitfall 5: No Audit Trail for Critical State Changes

**What goes wrong:**
When something goes wrong (task marked complete erroneously, plan archived accidentally), there's no record of who made the change, when, or why. Debugging involves guessing, logs are insufficient, and recovery is risky because you don't know what changed. Security incidents are untraceable.

**Why it happens:**
Audit tables are considered "overhead" or "nice to have" for an MVP. Logging captures the operation but not the before/after state. Developers focus on the happy path and forget the "what happened when it didn't work" path.

**How to avoid:**
Maintain a `plan_history` and `task_history` table tracking all status changes. Insert history record before applying the change:
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

-- Insert before every status update
INSERT INTO plan_history (plan_id, old_status, new_status, changed_by)
VALUES ($1, $2, $3, $4);
```

**Warning signs:**
- Questions like "who completed this task?" have no answer
- Manually editing database status values to "fix" issues
- Security review asks "can we track who changed what?" and answer is no

**Phase to address:**
Phase 1 (Database Schema) - Create history tables alongside main tables. Phase 2 (Storage Layer) - Insert history records before every status change. This is not optional; it's essential data.

---

## Technical Debt Patterns

| Shortcut | Immediate Benefit | Long-term Cost | When Acceptable |
|----------|-------------------|----------------|-----------------|
| Skip audit history tables in Phase 1 | Fewer tables, faster initial shipping | Cannot debug production issues, security events untraceable | Never - implement from start |
| Use in-memory "active plan" cache to avoid DB query | Faster reads, simpler code | Cache invalidation bugs, scaling issues with multiple instances | MVP only, single-instance deployment |
| Put all validation in service layer only | Faster development, simpler DB schema | Direct DB access can create invalid states, harder to recover | Never - use CHECK constraints |
| Combine PlanService and TaskService initially | Fewer files, simpler dependency graph | Monolithic service, harder to test, unclear responsibilities | Acceptable for Phase 1-2, split later |
| Use simple status strings instead of enums | No code generation needed, flexible | Runtime errors on typos, no IntelliSense support | Never - use enums with CHECK constraint |

## Integration Gotchas

| Integration | Common Mistake | Correct Approach |
|-------------|----------------|------------------|
| **PostgreSQL transaction handling** | Opening connection but forgetting to close/rollback on exception | Use `await using` for both connection and transaction with try/catch/rollback |
| **Npgsql parameter handling** | String concatenation in SQL queries | Always use `NpgsqlParameter` or `@param` syntax to prevent SQL injection |
| **MCP tool registration** | Adding tools but not updating tool list schema | Maintain tool registry array, auto-generate JSON schema from registration |
| **CLI command parsing** | Hard-coding argument positions | Use System.CommandLine or similar library for robust parsing |
| **Connection pooling** | Opening new connection for every operation | Use Npgsql connection pooling (default), reuse connections within scope |
| **DateTime handling** | Mixing UTC/local times, storing server time | Always store UTC timestamps (TIMESTAMPTZ), convert only at presentation layer |

## Performance Traps

| Trap | Symptoms | Prevention | When It Breaks |
|------|----------|------------|----------------|
| **N+1 query for tasks** | Plan list loads fast, but fetching all tasks triggers one query per plan | Use JOIN or IN clause to batch load tasks, or JSON aggregation | At 100+ plans with average 10+ tasks each |
| **Missing composite index on (session_id, status)** | Active plan lookup gets slower over time | Add `CREATE INDEX ix_plans_session_status ON plans(session_id, status)` | At 10k+ plans, especially with frequent active plan queries |
| **Full table scans on LIKE queries** | Search by plan name becomes slow at scale | Use pgvector for semantic search or prefix indexes, not full LIKE on text | At 100k+ plans, frequent name searches |
| **History table growing unbounded** | Database size balloons, performance degrades | Implement history archival/purging strategy (keep last N months, archive older) | At 1M+ history records, depending on query patterns |
| **No pagination on plan list APIs** | Plan list endpoint timeouts at scale | Always return paginated results with continuation tokens | At 1k+ plans returned in single call |

## Security Mistakes

| Mistake | Risk | Prevention |
|---------|------|------------|
| **Session ID injection** | Malicious agents can access other sessions' plans | Validate session ID belongs to authenticated caller, use opaque tokens instead of raw IDs |
| **No authorization on plan operations** | Any session can update any plan's tasks | Always check `plan.session_id == caller.session_id` before allowing updates |
| **SQL injection via user input** | Plan names or task titles could contain malicious SQL | Always parameterize queries, never concatenate user input into SQL |
| **Missing audit trail** | Cannot trace unauthorized plan changes | History table with `changed_by` field is mandatory |
| **Soft delete without access control** | Archived plans still accessible via direct ID lookup | Apply access control to archived plans same as active ones, or encrypt/archive separately |

## UX Pitfalls

| Pitfall | User Impact | Better Approach |
|---------|-------------|-----------------|
| **Returning plan ID without plan object** | Agent receives ID but must make second call to get details | Return full plan object on creation (already available from INSERT) |
| **No confirmation on destructive operations** | Accidentally archived plan with no way to recover | Require explicit confirmation parameter or separate "archive" vs "delete" operations |
| **Status enums exposed as strings to consumers** | Breaking changes when adding new status values | Use string representation but maintain numeric IDs internally, version API schema |
| **Error messages are generic "operation failed"** | Agent cannot distinguish between "plan not found" vs "permission denied" | Return specific error codes with human-readable messages |
| **No progress indication for bulk operations** | Agent marks many tasks complete but UX shows nothing until all done | Return incremental updates or provide progress endpoint |

## "Looks Done But Isn't" Checklist

- [ ] **Plan creation with initial tasks:** Often missing transaction — verify plan AND tasks created atomically by checking DB state between steps
- [ ] **Task completion auto-updating plan status:** Often missing logic — verify all tasks completed → plan status changes automatically
- [ ] **Archive operation:** Often missing task cascade delete — verify archived plans' tasks are also removed or archived
- [ ] **Concurrent plan activation:** Often missing locking — verify two sessions cannot activate conflicting plans simultaneously
- [ ] **History recording:** Often missing on status changes — verify every status update inserts history record
- [ ] **Status validation:** Often missing on non-API entry points — verify all code paths that update status call transition validator
- [ ] **Error handling on missing plans:** Often returning null instead of throwing — verify missing plan throws descriptive exception
- [ ] **Session scope enforcement:** Often missing on read operations — verify agents can only read their own session's plans

## Recovery Strategies

| Pitfall | Recovery Cost | Recovery Steps |
|---------|---------------|----------------|
| **Orphaned tasks (plan deleted, tasks remain)** | LOW | Run cleanup script: `DELETE FROM tasks WHERE plan_id NOT IN (SELECT id FROM plans)`; add foreign key constraint if missing |
| **Duplicate active plans** | MEDIUM | Identify duplicates, determine which is "real" (last updated), set others to Draft or Archived, add partial unique index |
| **Invalid status values** | MEDIUM | Identify invalid values via `SELECT DISTINCT status FROM plans`, map to closest valid value or set to Draft, add CHECK constraint |
| **Missing audit history** | HIGH | Cannot recover past events; add history tables going forward, annotate affected records with "pre-audit" marker |
| **Transaction orphan (half-completed operation)** | MEDIUM | Identify incomplete operations (plan with 0 tasks, task with missing plan), manually complete or delete, improve error handling |

## Pitfall-to-Phase Mapping

| Pitfall | Prevention Phase | Verification |
|---------|------------------|--------------|
| **State machine without enforcement** | Phase 1 (Database Schema) - Add CHECK constraints for status enums | Phase 1 tests: Verify invalid status values rejected by database |
| **Race condition on "one active plan"** | Phase 2 (Storage Layer) - Implement SELECT FOR UPDATE or advisory locks | Phase 2 tests: Concurrent plan creation attempts fail gracefully |
| **Partial transaction failure orphaning tasks** | Phase 2 (Storage Layer) - All multi-step operations in transactions | Phase 2 tests: Simulate failures mid-operation, verify atomic rollback |
| **Scattered status transition logic** | Phase 1 (Domain Models) - Central transition validator class | Phase 3 tests: All status changes go through validator, no direct status assignments |
| **No audit trail** | Phase 1 (Database Schema) - Create history tables | Phase 2 tests: Every status change creates history record |
| **N+1 query for tasks** | Phase 2 (Storage Layer) - Batch load with JOIN/IN | Phase 2 load tests: Query count scales with plan count, not task count |
| **Missing composite index** | Phase 1 (Database Schema) - Add index on (session_id, status) | Phase 3 load tests: Active plan lookup time constant regardless of total plans |
| **Session ID injection** | Phase 4 (MCP Integration) - Validate session ownership | Phase 3 security tests: Cannot access plans from other sessions |

## Domain-Specific Mistakes for AI Agent Workflows

| Mistake | Why It Happens in AI Context | Prevention |
|---------|-----------------------------|------------|
| **Agents retrying idempotent operations** | Agent sees error from network glitch and retries, creating duplicate plans | Make plan creation idempotent: check if plan with same (sessionId, projectId, name) exists, return existing ID |
| **Over-long task lists** | LLM generates 20+ tasks for a simple feature, creating unwieldy plans | Validate task count during creation (e.g., max 10 tasks), require explicit confirmation for more |
| **Vague task descriptions** | Agent creates tasks like "fix the bug" without details | Enforce minimum task title length, require description field for complex tasks |
| **Abandoned plans cluttering DB** | Agent creates plan, crashes, new session creates new plan | Implement cleanup routine: archive plans older than X days with Draft status |
| **Session reuse across unrelated tasks** | Agent continues using same plan for unrelated work | Clear active plan when switching projects or after significant time gap |

## Sources

- Internal architecture analysis: `.planning/research/ARCHITECTURE.md` - Anti-patterns section (HIGH confidence)
- PostgreSQL transaction patterns: Common database design best practices (MEDIUM confidence)
- State machine implementation: General DDD patterns and workflow engine design principles (MEDIUM confidence)
- .NET async/await with transactions: Npgsql documentation patterns (MEDIUM confidence)
- MCP integration: Existing NebulaRAG MCP implementation patterns (HIGH confidence)

---

*Pitfalls research for: Plan/Task Lifecycle Management System*
*Researched: 2026-02-27*
