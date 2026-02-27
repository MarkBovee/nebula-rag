# Planner Prompt: MCP Tool Handlers

## Planning Requirements
Create a comprehensive plan for implementing MCP tool handlers with session validation for the NebulaRAG+ plan lifecycle management system.

## Context
- **Project:** NebulaRAG+ Plan Lifecycle Management
- **Current Phase:** 4 - MCP Integration
- **Previous Phase:** 3 - Service Layer (completed)
- **Technology Stack:** .NET 10, PostgreSQL, MCP
- **Core Value:** AI agents can reliably create, track, and complete execution plans with full persistence and retrieval

## Dependencies
- Phase 3: Service Layer (PlanService, TaskService, PlanValidator)
- Existing .NET 10 / PostgreSQL infrastructure
- MCP framework requirements

## Requirements to Cover
- MCP-01: Create plan tool
- MCP-02: Get plan tool
- MCP-03: List plans tool
- MCP-04: Update plan tool
- MCP-05: Complete task tool
- MCP-06: Update task tool
- MCP-07: Archive plan tool
- ERROR-03: Session validation error handling

## Success Criteria
1. All MCP tools validate session ownership
2. Proper error handling for invalid sessions
3. Integration with existing service layer
4. Compliance with MCP specification
5. Security through session validation

## Files to Create/Modify
- src/Services/PlanMcpTool.cs
- src/Services/SessionValidator.cs
- src/Exceptions/PlanException.cs (extend if needed)

## Plan Structure
Create a detailed plan covering:
1. Session validation middleware implementation
2. PlanMcpTool class structure
3. Individual tool handler implementations
4. Error handling strategy
5. Integration approach with existing services
6. Testing strategy

## Output Format
Provide a comprehensive plan document with clear objectives, tasks, dependencies, and success criteria.