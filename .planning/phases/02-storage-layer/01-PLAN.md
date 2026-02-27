---
phase: 02-storage-layer
plan: 01
type: execute
wave: 1
depends_on: []
files_modified:
  - src/NebulaRAG.Core/Storage/PostgresPlanStore.cs
  - src/NebulaRAG.Core/Exceptions/PlanNotFoundException.cs
autonomous: true
requirements:
  - PLAN-02
  - PLAN-03
  - PLAN-04
  - PLAN-05
  - PLAN-08
  - TASK-01
  - TASK-02
  - AUDIT-01
  - AUDIT-02
  - AUDIT-03
  - PERF-04
  - ERROR-04
user_setup: []

must_haves:
  truths:
    - PostgresPlanStore provides CRUD operations for plans and tasks
    - Plan creation with initial tasks is atomic (all or none within single transaction)
    - Every plan and task status change creates a history record with changed_by, old_status, new_status, timestamp, reason
    - Querying by projectId+name or planId returns plan with all tasks
    - Missing plans or tasks throw PlanNotFoundException with descriptive error
  artifacts:
    - path: "src/NebulaRAG.Core/Storage/PostgresPlanStore.cs"
      provides: "CRUD operations with transaction integrity and history tracking"
      min_lines: 400
    - path: "src/NebulaRAG.Core/Exceptions/PlanNotFoundException.cs"
      provides: "Custom exception for missing plan/task scenarios"
      min_lines: 30
  key_links:
    - from: "CreatePlanAsync transaction"
      to: "PostgresPlanStore.InitializeSchemaAsync"
      via: "Uses same connection string and Npgsql patterns"
    - from: "History record creation"
      to: "PlanModels.cs PlanHistoryRecord/TaskHistoryRecord"
      via: "Matches record constructor signature exactly"
---

# Plan 01: Storage Layer - CRUD Operations with Transaction Integrity

**Created:** 2026-02-27
**Phase:** 2 - Storage Layer
**Status:** Draft
**Wave:** 1

## Overview

This plan implements the data access layer for NebulaRAG+ Plan Lifecycle Management, providing CRUD operations for plans and tasks with full transaction integrity and automatic history tracking. All operations use PostgreSQL transactions to ensure atomicity, and every status change creates an audit trail entry. The layer follows existing NebulaRAG patterns from PostgresRagStore for consistency.

## Success Criteria (from Roadmap)

1. PostgresPlanStore provides CRUD operations for plans and tasks
2. Plan creation with initial tasks is atomic (all or none within single transaction)
3. Every plan and task status change creates a history record with changed_by, old_status, new_status, timestamp, reason
4. Querying by projectId+name or planId returns plan with all tasks
5. Missing plans or tasks throw PlanNotFoundException with descriptive error

## Dependencies

- Phase 1: Database Schema & Domain Models (PostgresPlanStore.cs, PlanModels.cs)

## Tasks

<task type="auto">
  <name>Task 1: Create PlanNotFoundException Custom Exception</name>
  <files>src/NebulaRAG.Core/Exceptions/PlanNotFoundException.cs</files>
  <action>Create a custom exception class for missing plan/task scenarios.

1. **Namespace**: NebulaRAG.Core.Exceptions

2. **Inheritance**: Inherit from System.Exception

3. **Constructors**:
   - Default constructor
   - Constructor with message string
   - Constructor with message string and inner exception
   - Constructor with plan identifier (long planId) and optional task identifier (long? taskId)
     - Auto-generates descriptive message: "Plan {planId} not found" or "Task {taskId} in plan {planId} not found"

4. **Properties**:
   - PlanId (long): The plan identifier that was not found
   - TaskId (long?): The optional task identifier that was not found

5. **XML Documentation**:
   - Class-level &lt;summary&gt; explaining the exception purpose
   - &lt;param&gt; tags for all constructor parameters
   - &lt;summary&gt; tags for properties

Follow the same pattern as RagException.cs in the same directory.</action>
  <verify>dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj</verify>
  <done>File exists at correct path, inherits from Exception, has all 4 constructors, PlanId and TaskId properties present, XML documentation complete, build succeeds with 0 errors 0 warnings</done>
</task>

<task type="auto">
  <name>Task 2: Add Plan CRUD Operations to PostgresPlanStore</name>
  <files>src/NebulaRAG.Core/Storage/PostgresPlanStore.cs</files>
  <action>Add CRUD methods for plans following PostgresRagStore patterns.

1. **GetPlanByIdAsync**:
   - Parameters: long planId, CancellationToken cancellationToken
   - Returns: Task&lt;PlanRecord&gt;
   - Throws: PlanNotFoundException if plan not found
   - SQL: SELECT id, project_id, session_id, name, description, status, created_at, updated_at, metadata FROM plans WHERE id = @planId
   - Use await using var connection = new NpgsqlConnection(_connectionString)
   - Use await connection.OpenAsync(cancellationToken)
   - Use await using var command = new NpgsqlCommand(sql, connection)
   - Add parameter: command.Parameters.AddWithValue("planId", planId)
   - Use await using var reader = await command.ExecuteReaderAsync(cancellationToken)
   - Call ReadPlanFromReader helper to convert reader to PlanRecord
   - Throw PlanNotFoundException if reader returns no rows

2. **GetPlanByProjectAndNameAsync**:
   - Parameters: string projectId, string name, CancellationToken cancellationToken
   - Returns: Task&lt;PlanRecord&gt;
   - Throws: PlanNotFoundException if plan not found
   - SQL: SELECT id, project_id, session_id, name, description, status, created_at, updated_at, metadata FROM plans WHERE project_id = @projectId AND name = @name LIMIT 1
   - Same connection/command pattern as GetPlanByIdAsync
   - Throw PlanNotFoundException if no rows returned

3. **CreatePlanAsync** (handles plan + initial tasks atomically):
   - Parameters: CreatePlanRequest request, CancellationToken cancellationToken
   - CreatePlanRequest record with: ProjectId (string), SessionId (string), Name (string), Description (string?), InitialTasks (IReadOnlyList&lt;CreateTaskRequest&gt;), ChangedBy (string)
   - CreateTaskRequest record with: Title (string), Description (string?), Priority (string)
   - Returns: Task&lt;(long planId, IReadOnlyList&lt;long&gt; taskIds)&gt;
   - All operations in single NpgsqlTransaction
   - SQL for plan insert: INSERT INTO plans (project_id, session_id, name, description, status, created_at, updated_at, metadata) VALUES (@projectId, @sessionId, @name, @description, @status, NOW(), NOW(), '{}'::jsonb) RETURNING id
   - Get planId from RETURNING clause
   - For each initial task: INSERT INTO tasks (plan_id, title, description, priority, status, created_at, updated_at, metadata) VALUES (@planId, @title, @description, @priority, @status, NOW(), NOW(), '{}'::jsonb) RETURNING id
   - Collect all taskIds
   - Create initial plan history: INSERT INTO plan_history (plan_id, old_status, new_status, changed_by, changed_at, reason) VALUES (@planId, NULL, @status, @changedBy, NOW(), @reason)
   - Call await transaction.CommitAsync(cancellationToken)
   - Return (planId, taskIds)

4. **UpdatePlanAsync**:
   - Parameters: long planId, UpdatePlanRequest request, CancellationToken cancellationToken
   - UpdatePlanRequest record with: Name (string?), Description (string?), ChangedBy (string)
   - Returns: Task
   - Throws: PlanNotFoundException if plan not found
   - First fetch current plan status for history
   - SQL for update: UPDATE plans SET name = COALESCE(@name, name), description = @description, updated_at = NOW() WHERE id = @planId
   - Return true if rows affected > 0, throw PlanNotFoundException otherwise

5. **ArchivePlanAsync**:
   - Parameters: long planId, string changedBy, string? reason, CancellationToken cancellationToken
   - Returns: Task
   - Throws: PlanNotFoundException if plan not found
   - Fetch current status, store in oldStatus variable
   - SQL: UPDATE plans SET status = 'archived', updated_at = NOW() WHERE id = @planId
   - Create history record with oldStatus, newStatus='archived', changedBy, changedAt=NOW(), reason
   - Return true if rows affected > 0, throw PlanNotFoundException otherwise

6. **ListPlansBySessionAsync**:
   - Parameters: string sessionId, CancellationToken cancellationToken
   - Returns: Task&lt;IReadOnlyList&lt;PlanRecord&gt;&gt;
   - SQL: SELECT id, project_id, session_id, name, description, status, created_at, updated_at, metadata FROM plans WHERE session_id = @sessionId ORDER BY created_at DESC
   - Use connection/command pattern, iterate reader with ReadPlanFromReader

7. **ReadPlanFromReader helper method**:
   - Private static method
   - Parameters: NpgsqlDataReader reader
   - Returns: PlanRecord
   - Extract all columns: id (long), project_id (string), session_id (string), name (string), description (string?), status (string), created_at (DateTime), updated_at (DateTime), metadata (string or JsonDocument)
   - Parse status string to PlanStatus enum using Enum.Parse&lt;PlanStatus&gt;
   - Parse metadata JSON to JsonDocument using JsonDocument.Parse
   - Create PlanRecord with all values

8. **XML Documentation** on all public methods:
   - &lt;summary&gt; explaining purpose
   - &lt;param&gt; tags for all parameters
   - &lt;returns&gt; tags for return values
   - &lt;exception&gt; tags for thrown exceptions

Follow the exact patterns from PostgresRagStore for connection management, parameter binding, and error handling.</action>
  <verify>dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj</verify>
  <done>All 7 methods added, CreatePlanAsync uses single NpgsqlTransaction, all methods use await using pattern, parameters use AddWithValue, PlanNotFoundException thrown when appropriate, XML documentation complete, build succeeds with 0 errors 0 warnings</done>
</task>

<task type="auto">
  <name>Task 3: Add Task CRUD Operations to PostgresPlanStore</name>
  <files>src/NebulaRAG.Core/Storage/PostgresPlanStore.cs</files>
  <action>Add CRUD methods for tasks following PostgresRagStore patterns.

1. **GetTasksByPlanIdAsync**:
   - Parameters: long planId, CancellationToken cancellationToken
   - Returns: Task&lt;IReadOnlyList&lt;PlanTaskRecord&gt;&gt;
   - SQL: SELECT id, plan_id, title, description, priority, status, created_at, updated_at, metadata FROM tasks WHERE plan_id = @planId ORDER BY created_at
   - Use connection/command pattern
   - Iterate reader, call ReadTaskFromReader for each row
   - Return list as IReadOnlyList&lt;PlanTaskRecord&gt;

2. **GetTaskByIdAsync**:
   - Parameters: long taskId, CancellationToken cancellationToken
   - Returns: Task&lt;PlanTaskRecord&gt;
   - Throws: PlanNotFoundException if task not found
   - SQL: SELECT id, plan_id, title, description, priority, status, created_at, updated_at, metadata FROM tasks WHERE id = @taskId
   - Call ReadTaskFromReader
   - Throw PlanNotFoundException if no rows returned

3. **CreateTaskAsync**:
   - Parameters: long planId, CreateTaskRequest request, CancellationToken cancellationToken
   - CreateTaskRequest: Title (string), Description (string?), Priority (string), ChangedBy (string)
   - Returns: Task&lt;long&gt;
   - Throws: PlanNotFoundException if plan not found (verify plan exists first)
   - SQL for insert: INSERT INTO tasks (plan_id, title, description, priority, status, created_at, updated_at, metadata) VALUES (@planId, @title, @description, @priority, @status, NOW(), NOW(), '{}'::jsonb) RETURNING id
   - Get taskId from RETURNING clause
   - Create task history: INSERT INTO task_history (task_id, old_status, new_status, changed_by, changed_at, reason) VALUES (@taskId, NULL, @status, @changedBy, NOW(), @reason)
   - Return taskId

4. **UpdateTaskAsync**:
   - Parameters: long taskId, UpdateTaskRequest request, CancellationToken cancellationToken
   - UpdateTaskRequest: Title (string?), Description (string?), Priority (string?), ChangedBy (string)
   - Returns: Task
   - Throws: PlanNotFoundException if task not found
   - Fetch current task status for history
   - SQL: UPDATE tasks SET title = COALESCE(@title, title), description = @description, priority = COALESCE(@priority, priority), updated_at = NOW() WHERE id = @taskId
   - Return true if rows affected > 0, throw PlanNotFoundException otherwise

5. **CompleteTaskAsync**:
   - Parameters: long taskId, string changedBy, string? reason, CancellationToken cancellationToken
   - Returns: Task
   - Throws: PlanNotFoundException if task not found
   - Fetch current status, store in oldStatus variable
   - SQL: UPDATE tasks SET status = 'completed', updated_at = NOW() WHERE id = @taskId
   - Create history record with oldStatus, newStatus='completed', changedBy, changedAt=NOW(), reason
   - Return true if rows affected > 0, throw PlanNotFoundException otherwise

6. **ReadTaskFromReader helper method**:
   - Private static method
   - Parameters: NpgsqlDataReader reader
   - Returns: PlanTaskRecord
   - Extract all columns: id (long), plan_id (long), title (string), description (string?), priority (string), status (string), created_at (DateTime), updated_at (DateTime), metadata (string or JsonDocument)
   - Parse status string to TaskStatus enum using Enum.Parse&lt;TaskStatus&gt;
   - Parse metadata JSON to JsonDocument using JsonDocument.Parse
   - Create PlanTaskRecord with all values

7. **XML Documentation** on all public methods:
   - &lt;summary&gt; explaining purpose
   - &lt;param&gt; tags for all parameters
   - &lt;returns&gt; tags for return values
   - &lt;exception&gt; tags for thrown exceptions

Follow the same connection/command patterns as plan operations and PostgresRagStore.</action>
  <verify>dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj</verify>
  <done>All 6 methods added, CreateTaskAsync creates history record, CompleteTaskAsync creates history record, GetTasksByPlanIdAsync returns ordered list, ReadTaskFromReader parses status enum and metadata, XML documentation complete, build succeeds with 0 errors 0 warnings</done>
</task>

<task type="auto">
  <name>Task 4: Add History Query Operations to PostgresPlanStore</name>
  <files>src/NebulaRAG.Core/Storage/PostgresPlanStore.cs</files>
  <action>Add query methods for plan and task history.

1. **GetPlanHistoryAsync**:
   - Parameters: long planId, CancellationToken cancellationToken
   - Returns: Task&lt;IReadOnlyList&lt;PlanHistoryRecord&gt;&gt;
   - SQL: SELECT id, plan_id, old_status, new_status, changed_by, changed_at, reason FROM plan_history WHERE plan_id = @planId ORDER BY changed_at DESC
   - Use connection/command pattern
   - Call ReadPlanHistoryFromReader for each row
   - Return list as IReadOnlyList&lt;PlanHistoryRecord&gt;

2. **GetTaskHistoryAsync**:
   - Parameters: long taskId, CancellationToken cancellationToken
   - Returns: Task&lt;IReadOnlyList&lt;TaskHistoryRecord&gt;&gt;
   - SQL: SELECT id, task_id, old_status, new_status, changed_by, changed_at, reason FROM task_history WHERE task_id = @taskId ORDER BY changed_at DESC
   - Use connection/command pattern
   - Call ReadTaskHistoryFromReader for each row
   - Return list as IReadOnlyList&lt;TaskHistoryRecord&gt;

3. **ReadPlanHistoryFromReader helper method**:
   - Private static method
   - Parameters: NpgsqlDataReader reader
   - Returns: PlanHistoryRecord
   - Extract all columns: id (long), plan_id (long), old_status (string?), new_status (string), changed_by (string), changed_at (DateTime), reason (string?)
   - Create PlanHistoryRecord with all values

4. **ReadTaskHistoryFromReader helper method**:
   - Private static method
   - Parameters: NpgsqlDataReader reader
   - Returns: TaskHistoryRecord
   - Extract all columns: id (long), task_id (long), old_status (string?), new_status (string), changed_by (string), changed_at (DateTime), reason (string?)
   - Create TaskHistoryRecord with all values

5. **XML Documentation** on all public methods:
   - &lt;summary&gt; explaining purpose
   - &lt;param&gt; tags for all parameters
   - &lt;returns&gt; tags for return values

These methods enable audit trail queries for compliance and debugging.</action>
  <verify>dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj</verify>
  <done>All 4 items added, history queries ordered by changed_at DESC, ReadPlanHistoryFromReader handles nullable old_status, ReadTaskHistoryFromReader handles nullable old_status, XML documentation complete, build succeeds with 0 errors 0 warnings</done>
</task>

<task type="auto">
  <name>Task 5: Add Aggregated Query Methods to PostgresPlanStore</name>
  <files>src/NebulaRAG.Core/Storage/PostgresPlanStore.cs</files>
  <action>Add convenience methods that return plans with their tasks in a single call.

1. **GetPlanWithTasksByIdAsync**:
   - Parameters: long planId, CancellationToken cancellationToken
   - Returns: Task&lt;(PlanRecord plan, IReadOnlyList&lt;PlanTaskRecord&gt; tasks)&gt;
   - Throws: PlanNotFoundException if plan not found
   - Implementation: Call GetPlanByIdAsync, then call GetTasksByPlanIdAsync, return tuple

2. **GetPlanWithTasksByProjectAndNameAsync**:
   - Parameters: string projectId, string name, CancellationToken cancellationToken
   - Returns: Task&lt;(PlanRecord plan, IReadOnlyList&lt;PlanTaskRecord&gt; tasks)&gt;
   - Throws: PlanNotFoundException if plan not found
   - Implementation: Call GetPlanByProjectAndNameAsync, then call GetTasksByPlanIdAsync, return tuple

3. **XML Documentation** on both methods:
   - &lt;summary&gt; explaining that this is a convenience method combining two queries
   - &lt;param&gt; tags for all parameters
   - &lt;returns&gt; tags explaining the tuple structure
   - &lt;exception&gt; tags for PlanNotFoundException

These methods are optimized for the common pattern of fetching a plan and all its tasks together.</action>
  <verify>dotnet build src/NebulaRAG.Core/NebulaRAG.Core.csproj</verify>
  <done>Both methods added, methods delegate to existing GetPlan* and GetTasksByPlanId methods, return tuples with plan and tasks, XML documentation complete, build succeeds with 0 errors 0 warnings</done>
</task>

## Verification Criteria

After completing all tasks, verify:

1. [ ] PlanNotFoundException.cs exists with all 4 constructors
2. [ ] PostgresPlanStore.cs compiles with 0 errors and 0 warnings
3. [ ] CreatePlanAsync uses single NpgsqlTransaction for atomicity
4. [ ] All plan status changes (archive, update) create plan_history records
5. [ ] All task status changes (complete, update) create task_history records
6. [ ] History records include: plan_id/task_id, old_status, new_status, changed_by, changed_at, reason
7. [ ] Missing plans/tasks throw PlanNotFoundException with descriptive message
8. [ ] GetPlanWithTasks* methods return both plan and tasks in single call
9. [ ] All methods use async/await with CancellationToken support
10. [ ] All SQL uses parameterized queries (no string concatenation)
11. [ ] All methods follow PostgresRagStore patterns for consistency
12. [ ] XML documentation covers all public methods and types

## Testing Strategy

Phase 3 (Service Layer) will include unit tests for storage operations to verify transaction integrity and history tracking. This phase focuses on implementation; compilation success and code review verify correctness.

## Notes

- Follow existing NebulaRAG patterns from PostgresRagStore for consistency
- Use NpgsqlTransaction for all multi-step operations requiring atomicity
- Use AddWithValue for all SQL parameter binding (no string interpolation in SQL)
- Use await using var for connection/command/reader disposal
- Enum.Parse&lt;PlanStatus&gt; and Enum.Parse&lt;TaskStatus&gt; convert database strings to enums
- JsonDocument.Parse converts database JSONB to JsonDocument
- History records are created synchronously within the same transaction as status changes
- Default status for new plans: 'draft' (from CONTEXT.md)
- Default status for new tasks: 'pending' (from CONTEXT.md)
