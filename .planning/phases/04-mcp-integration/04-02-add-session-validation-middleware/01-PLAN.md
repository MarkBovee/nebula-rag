# 04-02: Add Session Validation Middleware

## Objective
Implement session validation middleware to enforce session ownership for all MCP plan management operations.

## Dependencies
- Phase 3: Service Layer (PlanService, TaskService, PlanValidator)
- Phase 4: 04-01 Create MCP Tool Handlers

## Requirements
- ERROR-03: Session validation error handling
- MCP-01: Create plan tool (with session validation)
- MCP-02: Get plan tool (with session validation)
- MCP-03: List plans tool (with session validation)
- MCP-04: Update plan tool (with session validation)
- MCP-05: Complete task tool (with session validation)
- MCP-06: Update task tool (with session validation)
- MCP-07: Archive plan tool (with session validation)

## Success Criteria
1. SessionValidator middleware validates session ownership for all MCP requests
2. Invalid session access throws PlanException with clear error message
3. Session validation middleware integrates with existing MCP transport handler
4. All MCP tools properly pass session context to validation middleware
5. Session validation is enforced before any plan operation is executed

## Tasks
1. Implement SessionValidator middleware class with session validation logic
2. Add session validation to PlanMcpTool class
3. Create custom PlanException for session validation errors
4. Update MCP transport handler to use session validation middleware
5. Test session validation with valid and invalid sessions
6. Ensure proper error handling for session validation failures

## Files to Modify/Create
- src/Services/SessionValidator.cs (create)
- src/Exceptions/PlanException.cs (extend)
- src/NebulaRAG.Mcp/Program.cs (update to include session validation)
- src/Services/PlanMcpTool.cs (update to use session validation)

## Notes
- Session validation must be performed before any plan operation
- Should check that the session ID in the request matches the session ID of the plan being accessed
- Must handle cases where plan doesn't exist or session doesn't match
- Error messages should be clear and informative for agents
- Follow existing exception handling patterns from Phase 3