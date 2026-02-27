# 04-01: MCP Tool Handlers - Execution Plan

## Execution Strategy
This plan will be executed in a single wave with sequential execution (parallelization: false).

## Wave 1: MCP Tool Handlers Implementation
**Plans:** 04-01-create-mcp-tool-handlers

**What it builds:** MCP tool handlers for plan management with session validation, enabling agent access to plan lifecycle operations while enforcing session ownership security.

**Technical Approach:**
1. Create SessionValidator middleware for session ownership enforcement
2. Implement PlanMcpTool class with all required MCP handlers
3. Integrate with existing service layer from Phase 3
4. Add session validation to all tool handlers
5. Create custom error handling for session violations

**Why it matters:** This completes the final layer of the architecture, exposing the service layer functionality to agents through secure MCP endpoints while maintaining the session ownership constraint.

## Tasks
1. Create SessionValidator class for session ownership validation
2. Implement PlanMcpTool class with create_plan handler
3. Implement get_plan and list_plans handlers
4. Implement update_plan, complete_task, and update_task handlers
5. Implement archive_plan handler
6. Add session validation middleware to all handlers
7. Create custom PlanException for session validation errors
8. Test MCP tool integration with existing services

## Dependencies
- Phase 3: Service Layer (PlanService, TaskService, PlanValidator)
- Existing .NET 10 / PostgreSQL infrastructure
- MCP framework integration

## Success Criteria
- All MCP tools validate session ownership
- Proper error handling for invalid sessions
- Integration with existing service layer
- Compliance with MCP specification
- Security through session validation