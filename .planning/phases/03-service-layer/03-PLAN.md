# Phase 3: Service Layer - Planning

**Created:** 2026-02-27
**Status:** Planning Complete
**Phase Goal:** Business logic enforcing plan lifecycle rules and active plan constraint

## Overview

This plan implements the service layer for NebulaRAG+ Plan Lifecycle Management, providing business logic enforcement for plan lifecycle rules and the active plan constraint. The service layer will coordinate with the storage layer (Phase 2) to enforce business rules while maintaining transaction integrity.

## Plans

### Plan 01: Service Layer Foundation

**Wave:** 1
**Depends on:** Phase 2 (Storage Layer)
**Files Modified:** src/NebulaRAG.Core/Services/PlanService.cs, src/NebulaRAG.Core/Services/TaskService.cs, src/NebulaRAG.Core/Services/PlanValidator.cs, src/NebulaRAG.Core/Exceptions/PlanException.cs

**Requirements:** PLAN-01, PLAN-07, AUDIT-04, ERROR-01, ERROR-02

**Tasks:**
```xml
<tasks>
  <task id="1" description="Create PlanException class for business rule violations">
    <steps>
      <step>Create custom PlanException class extending Exception</step>
      <step>Add properties for business rule context</step>
      <step>Implement constructors with descriptive messages</step>
    </steps>
  </task>

  <task id="2" description="Create PlanValidator class for status transition validation">
    <steps>
      <step>Create PlanValidator class with static validation methods</step>
      <step>Implement CanTransition method with transition rules</step>
      <step>Add validation for Draft → Active, Active → Completed, Active → Archived, Completed → Archived</step>
      <step>Block invalid transitions (Draft → Completed, Draft → Archived, etc.)</step>
    </steps>
  </task>

  <task id="3" description="Create PlanService class with business logic">
    <steps>
      <step>Create PlanService class with dependency on PostgresPlanStore</step>
      <step>Implement CreatePlan method with active plan check</step>
      <step>Add active plan constraint enforcement (check for existing active plan)</step>
      <step>Implement ArchivePlan method with status validation</step>
      <step>Add session isolation enforcement</step>
    </steps>
  </task>

  <task id="4" description="Create TaskService class with business logic">
    <steps>
      <step>Create TaskService class with dependency on PostgresPlanStore</step>
      <step>Implement CompleteTask method with status validation</step>
      <step>Add task status transition validation</step>
      <step>Implement UpdateTask method with business rules</step>
    </steps>
  </task>
</tasks>
```

**Verification Criteria:**
- PlanException class created with proper constructors
- PlanValidator correctly validates status transitions
- PlanService enforces active plan constraint
- TaskService validates task status transitions
- All services use PostgresPlanStore for data access

**must_haves:**
- Active plan constraint enforced at service layer
- Status transitions validated before storage operations
- Descriptive exceptions for business rule violations
- Services coordinate properly with storage layer
- No new transactions introduced (use storage layer transactions)

## Phase Summary

**Plans:** 1 plan in 1 wave
**Total Tasks:** 4 tasks
**Dependencies:** Phase 2 (Storage Layer) completed

---

*Phase 3 planning completed: 2026-02-27*