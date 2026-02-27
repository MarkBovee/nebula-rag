# Roadmap: NebulaRAG+ Plan Lifecycle Management

**Created:** 2026-02-27
**Depth:** Quick (3-5 phases)
**Coverage:** 30/30 v1 requirements mapped

## Phases

- [x] **Phase 1: Database Schema & Domain Models** - Foundation: tables, constraints, indexes, C# models
- [x] **Phase 2: Storage Layer** - Data access with transactions, CRUD operations, history tracking (completed 2026-02-27)
- [ ] **Phase 3: Service Layer** - Business logic, active plan enforcement, status validation
- [ ] **Phase 4: MCP Integration** - Tool handlers for agent access with session validation

## Phase Details

### Phase 1: Database Schema & Domain Models

**Goal:** Foundation layer enabling plan and task persistence with database-enforced constraints

**Depends on:** Nothing (first phase)

**Requirements:** PLAN-06, TASK-03, TASK-04, PERF-01, PERF-02, PERF-03

**Success Criteria** (what must be TRUE):
1. PostgreSQL tables created for plans, tasks, plan_history, task_history with proper relationships
2. CHECK constraints enforce valid status values at database level for plans and tasks
3. Composite indexes exist on (session_id, status) and (project_id, name) for efficient queries
4. C# domain models (Plan, PlanTask, PlanStatus, TaskStatus) map cleanly to database schema
5. Cascade delete configured so deleting a plan automatically removes its tasks

**Plans:** 2 files created (PlanModels.cs, PostgresPlanStore.cs)

---

### Phase 2: Storage Layer

**Goal:** Reliable data access with transaction integrity and audit trail support

**Depends on:** Phase 1 (Database Schema & Domain Models)

**Requirements:** PLAN-02, PLAN-03, PLAN-04, PLAN-05, PLAN-08, TASK-01, TASK-02, AUDIT-01, AUDIT-02, AUDIT-03, PERF-04, ERROR-04

**Success Criteria** (what must be TRUE):
1. PostgresPlanStore provides CRUD operations for plans and tasks
2. Plan creation with initial tasks is atomic (all or none within single transaction)
3. Every plan and task status change creates a history record with changed_by, old_status, new_status, timestamp, reason
4. Querying by projectId+name or planId returns plan with all tasks
5. Missing plans or tasks throw PlanNotFoundException with descriptive error

**Plans:** 1/1 plans complete

---

### Phase 3: Service Layer

**Goal:** Business logic enforcing plan lifecycle rules and active plan constraint

**Depends on:** Phase 2 (Storage Layer)

**Requirements:** PLAN-01, PLAN-07, AUDIT-04, ERROR-01, ERROR-02

**Success Criteria** (what must be TRUE):
1. Agent can create a plan with name, projectId, and initial tasks
2. Only one active plan can exist per session (attempting to create another throws PlanException)
3. Invalid status transitions throw descriptive exceptions (e.g., cannot transition Draft to Archived)
4. All status transitions are validated through centralized validator
5. PlanService and TaskService coordinate storage operations with proper business rules

**Plans:** TBD

---

### Phase 4: MCP Integration

**Goal:** Agent-accessible tools for plan management with session ownership enforcement

**Depends on:** Phase 3 (Service Layer)

**Requirements:** MCP-01, MCP-02, MCP-03, MCP-04, MCP-05, MCP-06, ERROR-03

**Success Criteria** (what must be TRUE):
1. MCP tool create_plan exposed and callable by agents
2. MCP tools get_plan and list_plans return plan data with tasks
3. MCP tools update_plan, complete_task, update_task modify plan/task data
4. MCP tool archive_plan transitions plan to Archived status
5. All MCP tools validate session ownership (caller cannot access other sessions' plans)
6. Attempting to modify another session's plan throws PlanException with clear error

**Plans:** 4/4 complete (04-01, 04-02, 04-03, 04-04)

---

## Progress

| Phase | Plans Complete | Status | Completed |
|-------|----------------|--------|-----------|
|1. Database Schema & Domain Models | 2/2 | Complete | ✓ |
|2. Storage Layer | 1/1 | Complete   | 2026-02-27 |
|3. Service Layer | 0/0 | Not started | - |
|4. MCP Integration | 4/4 | Complete | 2026-02-27 |

---
*Roadmap created: 2026-02-27*
*Last updated: 2026-02-27*
