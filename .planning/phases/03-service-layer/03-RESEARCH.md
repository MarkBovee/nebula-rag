# Phase 3 Research: Service Layer Implementation

## RESEARCH COMPLETE ✅

### Key Findings

**Current Architecture Analysis:**
- Phase 1: Database Schema & Domain Models completed with PlanStatus and TaskStatus enums
- Phase 2: Storage Layer completed with comprehensive PostgresPlanStore implementation
- Existing infrastructure includes: PlanRecord, PlanTaskRecord, PlanHistoryRecord, TaskHistoryRecord models
- Database schema enforces status constraints at database level
- PostgresPlanStore provides full CRUD operations with transaction support

**Service Layer Requirements:**
- PLAN-01: Create plan with name, projectId, and initial tasks
- PLAN-07: Enforce one active plan per session
- AUDIT-04: Validate status transitions (Draft → Active → Completed → Archived)
- ERROR-01: Invalid status transitions throw descriptive exceptions
- ERROR-02: PlanException when creating active plan when one exists

**Technical Approach:**

1. **Service Layer Architecture:**
   - PlanService: Handles plan-level operations (create, update, archive)
   - TaskService: Handles task-level operations (complete, update)
   - PlanValidator: Centralized status transition validation
   - PlanException: Custom exception for business rule violations

2. **Active Plan Constraint Enforcement:**
   - Check for existing active plan before creating new plan
   - Use PostgresPlanStore's ListPlansBySessionAsync to check session's active plans
   - Throw PlanException if active plan already exists

3. **Status Transition Validation:**
   - Centralized validator for all status transitions
   - Allow: Draft → Active, Active → Completed, Active → Archived, Completed → Archived
   - Block: Draft → Completed, Draft → Archived, Active → Draft, etc.
   - Validate transitions before updating storage

4. **Exception Handling:**
   - Extend PlanNotFoundException to include business rule violations
   - Create PlanException for business logic errors
   - Provide descriptive error messages

5. **Service Coordination:**
   - Services will use PostgresPlanStore for data access
   - Maintain transaction boundaries from storage layer
   - Coordinate between PlanService and TaskService

**Implementation Patterns:**

- **Service Classes:** PlanService, TaskService, PlanValidator
- **Exception Classes:** PlanException (new), extending existing PlanNotFoundException
- **Validation Logic:** Centralized validator with predefined transition rules
- **Dependency Injection:** Services will depend on PostgresPlanStore

**Critical Considerations:**

1. **Race Condition Prevention:** Active plan check must be atomic with plan creation
2. **Transaction Boundaries:** Services should not introduce new transactions; rely on storage layer
3. **Status Validation:** Must prevent invalid transitions at business logic level
4. **Error Propagation:** Business exceptions should be clearly distinguishable
5. **Session Isolation:** Enforce session ownership constraints

**Research Validation:**
- Database schema already enforces status constraints (CHECK constraints)
- Storage layer provides transaction support for atomic operations
- Existing exception handling pattern established
- MCP integration will be handled in Phase 4

---

*Research completed: 2026-02-27*