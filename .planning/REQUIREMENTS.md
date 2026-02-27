# Requirements: NebulaRAG+ Plan Lifecycle Management

**Defined:** 2026-02-27
**Core Value:** AI agents can reliably create, track, and complete execution plans with full persistence and retrieval.

## v1 Requirements

Requirements for initial release. Each maps to roadmap phases.

### Plan Management

- [ ] **PLAN-01**: Agent can create a plan with name, projectId, and initial tasks
- [x] **PLAN-02**: Agent can retrieve a plan by projectId + name
- [x] **PLAN-03**: Agent can retrieve a plan by plan ID
- [x] **PLAN-04**: Agent can update plan details (name, description)
- [x] **PLAN-05**: Agent can archive a plan when complete
- [x] **PLAN-06**: Plan has a status (Draft, Active, Completed, Archived) enforced at database level
- [ ] **PLAN-07**: Only one active plan can exist per session
- [x] **PLAN-08**: Plan creation with initial tasks is atomic (all or none)

### Task Management

- [x] **TASK-01**: Agent can mark a task as complete
- [x] **TASK-02**: Agent can update task details (title, description, priority)
- [x] **TASK-03**: Task has a status (Pending, InProgress, Completed, Failed) enforced at database level
- [x] **TASK-04**: Tasks cascade delete when parent plan is deleted

### Data Integrity & Auditing

- [x] **AUDIT-01**: Every plan status change creates a history record
- [x] **AUDIT-02**: Every task status change creates a history record
- [x] **AUDIT-03**: History records include changed_by (sessionId), old_status, new_status, timestamp, and reason
- [ ] **AUDIT-04**: Status transitions are validated (Draft -> Active -> Completed -> Archived)

### MCP Integration

- [ ] **MCP-01**: Expose plan creation as MCP tool (create_plan)
- [ ] **MCP-02**: Expose plan retrieval as MCP tool (get_plan, list_plans)
- [ ] **MCP-03**: Expose plan update as MCP tool (update_plan)
- [ ] **MCP-04**: Expose task operations as MCP tools (complete_task, update_task)
- [ ] **MCP-05**: Expose plan archival as MCP tool (archive_plan)
- [ ] **MCP-06**: All MCP tools validate session ownership (caller can only access their session's plans)

### Performance & Scalability

- [x] **PERF-01**: Composite index on (session_id, status) for active plan queries
- [x] **PERF-02**: Composite index on (project_id, name) for plan lookups
- [x] **PERF-03**: Index on tasks.plan_id for task list queries
- [x] **PERF-04**: Multi-step operations use PostgreSQL transactions

### Error Handling

- [ ] **ERROR-01**: Invalid status transitions throw descriptive exceptions
- [ ] **ERROR-02**: Attempting to create active plan when one exists throws PlanException
- [ ] **ERROR-03**: Attempting to modify plan from another session throws PlanException
- [x] **ERROR-04**: Missing plan or task returns descriptive error (not null)

## v2 Requirements

Deferred to future release. Tracked but not in current roadmap.

### Maintenance

- **MAINT-01**: Cleanup function removes archived plans older than configured retention period
- **MAINT-02**: Cleanup function cascades to plan_history and task_history

## Out of Scope

Explicitly excluded. Documented to prevent scope creep.

| Feature | Reason |
|---------|--------|
| Real-time multi-agent collaboration | Single agent per session, one active plan requirement |
| Plan templates | Agent generates fresh plans per request |
| Plan sharing between projects | Plans are scoped to projectId, explicit boundaries |
| External integrations (Notion, Jira) | PostgreSQL-only storage, focus on agent workflow |
| Plan export/import | MCP protocol is primary interface, not external file formats |

## Traceability

Which phases cover which requirements.

| Requirement | Phase | Status |
|-------------|-------|--------|
| PLAN-01 | Phase 3 | Pending |
| PLAN-02 | Phase 2 | Complete |
| PLAN-03 | Phase 2 | Complete |
| PLAN-04 | Phase 2 | Complete |
| PLAN-05 | Phase 2 | Complete |
| PLAN-06 | Phase 1 | Complete |
| PLAN-07 | Phase 3 | Pending |
| PLAN-08 | Phase 2 | Complete |
| TASK-01 | Phase 2 | Complete |
| TASK-02 | Phase 2 | Complete |
| TASK-03 | Phase 1 | Complete |
| TASK-04 | Phase 1 | Complete |
| AUDIT-01 | Phase 2 | Complete |
| AUDIT-02 | Phase 2 | Complete |
| AUDIT-03 | Phase 2 | Complete |
| AUDIT-04 | Phase 3 | Pending |
| MCP-01 | Phase 4 | Pending |
| MCP-02 | Phase 4 | Pending |
| MCP-03 | Phase 4 | Pending |
| MCP-04 | Phase 4 | Pending |
| MCP-05 | Phase 4 | Pending |
| MCP-06 | Phase 4 | Pending |
| PERF-01 | Phase 1 | Complete |
| PERF-02 | Phase 1 | Complete |
| PERF-03 | Phase 1 | Complete |
| PERF-04 | Phase 2 | Complete |
| ERROR-01 | Phase 3 | Pending |
| ERROR-02 | Phase 3 | Pending |
| ERROR-03 | Phase 4 | Pending |
| ERROR-04 | Phase 2 | Complete |

**Coverage:**
- v1 requirements: 30 total
- Mapped to phases: 30
- Unmapped: 0

---
*Requirements defined: 2026-02-27*
*Last updated: 2026-02-27 with roadmap phase mappings*
