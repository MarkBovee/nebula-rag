---
phase: 02-storage-layer
plan: 01
subsystem: Storage Layer
tags: [storage, postgres, crud, transactions]
requires:
  provides:
    - id: "PLAN-02"
      name: "Retrieve plan by projectId+name"
      description: "PostgresPlanStore.GetPlanByProjectAndNameAsync"
    - id: "PLAN-03"
      name: "Retrieve plan by plan ID"
      description: "PostgresPlanStore.GetPlanByIdAsync"
    - id: "PLAN-04"
      name: "Update plan details"
      description: "PostgresPlanStore.UpdatePlanAsync"
    - id: "PLAN-05"
      name: "Archive plan"
      description: "PostgresPlanStore.ArchivePlanAsync"
    - id: "PLAN-08"
      name: "Atomic plan creation with tasks"
      description: "PostgresPlanStore.CreatePlanAsync with NpgsqlTransaction"
    - id: "TASK-01"
      name: "Mark task complete"
      description: "PostgresPlanStore.CompleteTaskAsync"
    - id: "TASK-02"
      name: "Update task details"
      description: "PostgresPlanStore.UpdateTaskAsync"
    - id: "AUDIT-01"
      name: "Plan status history"
      description: "GetPlanHistoryAsync + plan_history records on status changes"
    - id: "AUDIT-02"
      name: "Task status history"
      description: "GetTaskHistoryAsync + task_history records on status changes"
    - id: "AUDIT-03"
      name: "History record details"
      description: "History includes changed_by, old_status, new_status, timestamp, reason"
    - id: "PERF-04"
      name: "Transaction integrity"
      description: "NpgsqlTransaction for multi-step operations"
    - id: "ERROR-04"
      name: "Missing plan/task errors"
      description: "PlanNotFoundException with descriptive messages"
affects:
  - "Phase 3: Service Layer (uses storage operations)"
tech-stack:
  added: []
  patterns:
    - NpgsqlTransaction for atomicity
    - Parameterized queries with AddWithValue
    - await using for resource disposal
    - Enum.Parse for status string conversion
    - Helper methods for reader parsing (ReadPlanFromReader, ReadTaskFromReader, etc.)
key-files:
  created:
    - path: "src/NebulaRAG.Core/Exceptions/PlanNotFoundException.cs"
      lines: 58
      description: "Custom exception for missing plan/task scenarios"
  modified:
    - path: "src/NebulaRAG.Core/Storage/PostgresPlanStore.cs"
      lines: 843
      description: "Added all CRUD operations, history queries, and aggregated methods"
key-decisions:
  - "Used fully qualified Models.TaskStatus to avoid ambiguity with System.Threading.Tasks.TaskStatus"
  - "PlanNotFoundException includes both PlanId and optional TaskId for programmatic access"
  - "History records use NULL old_status for initial status changes (new entities)"
  - "CreatePlanAsync uses single transaction for plan + tasks + initial history"
  - "CompleteTaskAsync and ArchivePlanAsync use transactions for status update + history record"
metrics:
  duration: "approximately 5 minutes"
  completed-date: "2026-02-27T08:36:00Z"
  tasks-completed: 5
  files-created: 1
  files-modified: 1
  total-lines-added: 901
---

# Phase 2 - Plan 01: Storage Layer - CRUD Operations with Transaction Integrity Summary

## Overview

Implemented the complete data access layer for NebulaRAG+ Plan Lifecycle Management, providing CRUD operations for plans and tasks with full transaction integrity and automatic history tracking. All operations use PostgreSQL transactions to ensure atomicity, and every status change creates an audit trail entry.

## One-Liner

PostgreSQL-based storage layer with full CRUD operations, atomic transactions, and automatic history tracking for plans and tasks.

## Tasks Completed

| Task | Name | Commit | Files |
| ---- | ----- | ------- | ------ |
| 1 | Create PlanNotFoundException Custom Exception | 479b69b | src/NebulaRAG.Core/Exceptions/PlanNotFoundException.cs |
| 2 | Add Plan CRUD Operations to PostgresPlanStore | 6562ff5 | src/NebulaRAG.Core/Storage/PostgresPlanStore.cs |
| 3 | Add Task CRUD Operations to PostgresPlanStore | f0e7efd | src/NebulaRAG.Core/Storage/PostgresPlanStore.cs |
| 4 | Add History Query Operations to PostgresPlanStore | ad66713 | src/NebulaRAG.Core/Storage/PostgresPlanStore.cs |
| 5 | Add Aggregated Query Methods to PostgresPlanStore | 97a1cf3 | src/NebulaRAG.Core/Storage/PostgresPlanStore.cs |

## Implementation Details

### Task 1: PlanNotFoundException Custom Exception
Created a custom exception class following the existing NebulaRAG pattern:
- Four constructors: default, message, message+inner, planId+taskId
- Auto-generates descriptive messages: "Plan {planId} not found" or "Task {taskId} in plan {planId} not found"
- PlanId and TaskId properties for programmatic access
- Full XML documentation

### Task 2: Plan CRUD Operations
Added comprehensive plan management methods:
- **GetPlanByIdAsync**: Retrieve plan by ID with PlanNotFoundException on missing data
- **GetPlanByProjectAndNameAsync**: Retrieve plan by projectId+name combination
- **CreatePlanAsync**: Atomic plan creation with initial tasks in single NpgsqlTransaction
- **UpdatePlanAsync**: Update plan name and description
- **ArchivePlanAsync**: Archive plan with history record in transaction
- **ListPlansBySessionAsync**: List all plans for a session ordered by created_at DESC
- **ReadPlanFromReader**: Helper method to parse NpgsqlDataReader to PlanRecord

Key patterns:
- Parameterized queries using AddWithValue (no SQL injection risk)
- await using for proper connection/command/reader disposal
- Enum.Parse<PlanStatus> for status string conversion
- JsonDocument.Parse for metadata JSONB parsing

### Task 3: Task CRUD Operations
Added complete task management methods:
- **GetTasksByPlanIdAsync**: Retrieve all tasks for a plan ordered by created_at
- **GetTaskByIdAsync**: Retrieve single task with PlanNotFoundException on missing
- **CreateTaskAsync**: Create task with initial history record in transaction
- **UpdateTaskAsync**: Update task title, description, priority
- **CompleteTaskAsync**: Mark task as completed with history record in transaction
- **ReadTaskFromReader**: Helper method to parse NpgsqlDataReader to PlanTaskRecord

All status changes create task_history records automatically with old_status, new_status, changed_by, changed_at, and reason.

### Task 4: History Query Operations
Added audit trail query methods:
- **GetPlanHistoryAsync**: Retrieve plan status change history ordered by changed_at DESC
- **GetTaskHistoryAsync**: Retrieve task status change history ordered by changed_at DESC
- **ReadPlanHistoryFromReader**: Helper to parse plan history records
- **ReadTaskHistoryFromReader**: Helper to parse task history records

Both helpers handle nullable old_status for initial status transitions (NULL old_status indicates first status).

### Task 5: Aggregated Query Methods
Added convenience methods for common patterns:
- **GetPlanWithTasksByIdAsync**: Combines GetPlanByIdAsync + GetTasksByPlanIdAsync in one call
- **GetPlanWithTasksByProjectAndNameAsync**: Combines GetPlanByProjectAndNameAsync + GetTasksByPlanIdAsync in one call

Both return tuples containing (plan, tasks) for simplified client code.

## Deviations from Plan

None - plan executed exactly as written.

**Fixed Issue during Task 2**:
- **[Rule 1 - Bug] Ambiguous TaskStatus reference**
  - **Found during:** Task 2 compilation
  - **Issue:** `TaskStatus` caused ambiguity between `NebulaRAG.Core.Models.TaskStatus` and `System.Threading.Tasks.TaskStatus`
  - **Fix:** Used fully qualified `Models.TaskStatus.Pending.ToString().ToLowerInvariant()` where needed
  - **Files modified:** src/NebulaRAG.Core/Storage/PostgresPlanStore.cs
  - **Commit:** 6562ff5 (included in Task 2 commit)

## Success Criteria Verification

All success criteria from the plan have been met:

1. PostgresPlanStore provides CRUD operations for plans and tasks
   - All plan CRUD: GetPlanByIdAsync, GetPlanByProjectAndNameAsync, CreatePlanAsync, UpdatePlanAsync, ArchivePlanAsync, ListPlansBySessionAsync
   - All task CRUD: GetTaskByIdAsync, GetTasksByPlanIdAsync, CreateTaskAsync, UpdateTaskAsync, CompleteTaskAsync

2. Plan creation with initial tasks is atomic (all or none within single transaction)
   - CreatePlanAsync uses NpgsqlTransaction wrapping plan insert, all task inserts, and plan history insert
   - Transaction commit only occurs after all operations succeed

3. Every plan and task status change creates a history record
   - CreatePlanAsync creates plan_history with old_status=NULL, new_status='draft'
   - ArchivePlanAsync creates plan_history with old_status and new_status='archived'
   - CreateTaskAsync creates task_history with old_status=NULL, new_status='pending'
   - CompleteTaskAsync creates task_history with old_status and new_status='completed'

4. Querying by projectId+name or planId returns plan with all tasks
   - GetPlanWithTasksByIdAsync returns (plan, tasks) tuple
   - GetPlanWithTasksByProjectAndNameAsync returns (plan, tasks) tuple

5. Missing plans or tasks throw PlanNotFoundException with descriptive error
   - All Get* methods throw PlanNotFoundException when no rows returned
   - Exception includes PlanId and optional TaskId for diagnostics

## Verification Criteria

1. PlanNotFoundException.cs exists with all 4 constructors - PASS
2. PostgresPlanStore.cs compiles with 0 errors and 0 warnings - PASS
3. CreatePlanAsync uses single NpgsqlTransaction for atomicity - PASS
4. All plan status changes create plan_history records - PASS (CreatePlanAsync, ArchivePlanAsync)
5. All task status changes create task_history records - PASS (CreateTaskAsync, CompleteTaskAsync)
6. History records include: plan_id/task_id, old_status, new_status, changed_by, changed_at, reason - PASS
7. Missing plans/tasks throw PlanNotFoundException with descriptive message - PASS
8. GetPlanWithTasks* methods return both plan and tasks in single call - PASS
9. All methods use async/await with CancellationToken support - PASS
10. All SQL uses parameterized queries (no string concatenation) - PASS
11. All methods follow PostgresRagStore patterns for consistency - PASS (await using, AddWithValue, connection pattern)
12. XML documentation covers all public methods and types - PASS

## Requirements Satisfied

This plan satisfies the following v1 requirements:

- **PLAN-02**: Agent can retrieve a plan by projectId + name (GetPlanByProjectAndNameAsync)
- **PLAN-03**: Agent can retrieve a plan by plan ID (GetPlanByIdAsync)
- **PLAN-04**: Agent can update plan details (UpdatePlanAsync)
- **PLAN-05**: Agent can archive a plan when complete (ArchivePlanAsync)
- **PLAN-08**: Plan creation with initial tasks is atomic (CreatePlanAsync with NpgsqlTransaction)
- **TASK-01**: Agent can mark a task as complete (CompleteTaskAsync)
- **TASK-02**: Agent can update task details (UpdateTaskAsync)
- **AUDIT-01**: Every plan status change creates a history record (plan_history table populated on CreatePlanAsync, ArchivePlanAsync)
- **AUDIT-02**: Every task status change creates a history record (task_history table populated on CreateTaskAsync, CompleteTaskAsync)
- **AUDIT-03**: History records include changed_by, old_status, new_status, timestamp, reason (all query methods return full history)
- **PERF-04**: Multi-step operations use PostgreSQL transactions (CreatePlanAsync, ArchivePlanAsync, CreateTaskAsync, CompleteTaskAsync all use NpgsqlTransaction)
- **ERROR-04**: Missing plan or task returns descriptive error (PlanNotFoundException thrown with planId/taskId context)

## Files Created/Modified

### Created
- `src/NebulaRAG.Core/Exceptions/PlanNotFoundException.cs` (58 lines)

### Modified
- `src/NebulaRAG.Core/Storage/PostgresPlanStore.cs` (843 lines, added 901 lines across all tasks)

## Next Steps

Phase 3: Service Layer will use this storage layer to implement business logic including:
- Active plan enforcement (only one active plan per session)
- Status transition validation
- Plan lifecycle management orchestration
