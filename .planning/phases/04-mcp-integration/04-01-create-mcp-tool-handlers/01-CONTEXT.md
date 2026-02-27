# 04-01 Context: MCP Integration Foundation

## Project Context
- **Current Phase:** 4 - MCP Integration
- **Previous Phase:** 3 - Service Layer (completed)
- **Technology Stack:** .NET 10, PostgreSQL, MCP (stdio and HTTP)
- **Core Value:** AI agents can reliably create, track, and complete execution plans with full persistence and retrieval

## Dependencies
- **Phase 3 Services:** PlanService, TaskService, PlanValidator
- **Existing Infrastructure:** PostgreSQL database with plan/task tables
- **MCP Requirements:** Session-based tool access with ownership validation

## Key Constraints
- Must integrate with existing .NET 10 / PostgreSQL stack
- Session validation is critical for security
- One active plan per session enforced at runtime
- All operations exposed via MCP endpoints for agent integration

## Available Components
- PlanService: Business logic for plan operations
- TaskService: Business logic for task operations
- PlanValidator: Status transition validation
- PostgresPlanStore: Data access layer
- Existing domain models (Plan, PlanTask, PlanStatus, TaskStatus)

## Session Management
- Session ownership must be enforced for all MCP tools
- Session validation middleware required
- Custom PlanException for session validation errors
- Error handling for invalid session access

## MCP Tool Requirements
- create_plan: Create new plan with tasks
- get_plan: Retrieve specific plan by ID
- list_plans: List plans for current session
- update_plan: Update plan details
- complete_task: Mark task as complete
- update_task: Update task details
- archive_plan: Archive completed plan

## Success Criteria
1. All MCP tools validate session ownership
2. Proper error handling for invalid sessions
3. Integration with existing service layer
4. Compliance with MCP specification
5. Security through session validation