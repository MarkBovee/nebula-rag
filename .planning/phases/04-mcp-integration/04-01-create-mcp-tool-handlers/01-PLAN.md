# 04-01: Create MCP Tool Handlers

## Objective
Implement MCP tool handlers for plan management operations with session ownership validation.

## Dependencies
- Phase 3: Service Layer (PlanService, TaskService, PlanValidator)
- Requires existing .NET 10 / PostgreSQL infrastructure

## Requirements
- MCP-01: Create plan tool
- MCP-02: Get plan tool
- MCP-03: List plans tool
- MCP-04: Update plan tool
- MCP-05: Complete task tool
- MCP-06: Update task tool
- MCP-07: Archive plan tool
- ERROR-03: Session validation error handling

## Success Criteria
1. MCP tool create_plan exposed and callable by agents
2. MCP tools get_plan and list_plans return plan data with tasks
3. MCP tools update_plan, complete_task, update_task modify plan/task data
4. MCP tool archive_plan transitions plan to Archived status
5. All MCP tools validate session ownership (caller cannot access other sessions' plans)
6. Attempting to modify another session's plan throws PlanException with clear error

## Tasks
1. Create PlanMcpTool class with session validation
2. Implement create_plan handler with PlanService integration
3. Implement get_plan handler with PlanService integration
4. Implement list_plans handler with PlanService integration
5. Implement update_plan handler with PlanService integration
6. Implement complete_task handler with PlanService integration
7. Implement update_task handler with PlanService integration
8. Implement archive_plan handler with PlanService integration
9. Add session validation middleware to all handlers
10. Create custom PlanException for session validation errors

## Files to Modify/Create
- .planning/phases/phase-4/04-01-create-mcp-tool-handlers.md (this file)
- src/Services/PlanMcpTool.cs
- src/Services/SessionValidator.cs
- src/Exceptions/PlanException.cs (extend if needed)

## Notes
- Must integrate with existing .NET 10 infrastructure
- Session validation is critical for security
- All tools should return appropriate error responses for invalid sessions
- Follow existing code patterns from Phase 3 services