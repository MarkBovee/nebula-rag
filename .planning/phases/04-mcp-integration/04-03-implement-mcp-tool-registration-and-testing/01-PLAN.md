# 04-03: Implement MCP Tool Registration and Testing

## Objective
Register MCP tools with the transport handler and implement comprehensive testing for plan management operations.

## Dependencies
- Phase 3: Service Layer (PlanService, TaskService, PlanValidator)
- Phase 4: 04-01 Create MCP Tool Handlers
- Phase 4: 04-02 Add Session Validation Middleware

## Requirements
- MCP-01: Create plan tool (registered and tested)
- MCP-02: Get plan tool (registered and tested)
- MCP-03: List plans tool (registered and tested)
- MCP-04: Update plan tool (registered and tested)
- MCP-05: Complete task tool (registered and tested)
- MCP-06: Update task tool (registered and tested)
- MCP-07: Archive plan tool (registered and tested)

## Success Criteria
1. All MCP tools properly registered with McpTransportHandler
2. Comprehensive testing of all plan management operations
3. Session validation tested with valid and invalid sessions
4. Error handling tested for edge cases
5. MCP tools return correct responses for successful and failed operations

## Tasks
1. Register all MCP tools with McpTransportHandler
2. Create integration tests for all MCP operations
3. Test session validation with valid session access
4. Test session validation with invalid session access
5. Test error handling for missing plans and invalid operations
6. Test successful plan creation, retrieval, update, and archiving
7. Test task operations (complete, update)
8. Ensure proper JSON-RPC compliance in responses

## Files to Modify/Create
- src/NebulaRAG.Mcp/Program.cs (update to register MCP tools)
- tests/NebulaRAG.Tests/Mcp/PlanMcpToolTests.cs (create)
- tests/NebulaRAG.Tests/Mcp/SessionValidationTests.cs (create)

## Notes
- Tests should cover both successful operations and error cases
- Must test session validation thoroughly
- Should verify JSON-RPC response format
- Integration tests should use real database connections
- Test coverage should be comprehensive for all MCP operations