# Phase 3: Service Layer - Context

**Gathered:** 2026-02-27
**Status:** Ready for planning
**Source:** Roadmap and Requirements Analysis

## Phase Boundary

**Goal:** Business logic enforcing plan lifecycle rules and active plan constraint

**What this phase delivers:**
- PlanService and TaskService classes implementing business logic
- Centralized status transition validation
- Active plan constraint enforcement (one active plan per session)
- Coordinated storage operations with proper business rules
- Comprehensive exception handling for invalid operations

## Implementation Decisions

### Business Logic Layer
- Service classes will implement the core business rules for plan lifecycle
- Status transitions will be validated through a centralized validator
- Active plan constraint will be enforced at runtime (one active plan per session)
- Services will coordinate with storage layer for data persistence

### Exception Handling
- Custom PlanException for business rule violations
- Descriptive error messages for invalid operations
- Proper exception propagation from service to storage layer

### Service Coordination
- PlanService handles plan-level operations (create, update, archive)
- TaskService handles task-level operations (complete, update)
- Services will use the PostgresPlanStore from Phase 2 for data access
- Transaction boundaries maintained from storage layer

### Status Validation
- Centralized status transition validator
- Validation of all status changes (Draft → Active → Completed → Archived)
- Prevention of invalid transitions (e.g., Draft to Archived)

## Specific Ideas

**Service Class Structure:**
- PlanService: Create, update, archive plans
- TaskService: Complete, update tasks
- PlanValidator: Centralized status transition validation
- PlanException: Custom exception for business rule violations

**Key Requirements to Address:**
- PLAN-01: Agent can create a plan with name, projectId, and initial tasks
- PLAN-07: Only one active plan can exist per session
- AUDIT-04: Status transitions are validated
- ERROR-01: Invalid status transitions throw descriptive exceptions
- ERROR-02: Attempting to create active plan when one exists throws PlanException

**Dependency on Phase 2:**
- PostgresPlanStore for data access
- Existing domain models (Plan, PlanTask, PlanStatus, TaskStatus)
- Transaction support from storage layer

## Deferred Ideas

None — all requirements for this phase are clearly defined and within scope.

---

*Phase: 03-service-layer*
*Context gathered: 2026-02-27 via roadmap and requirements analysis*