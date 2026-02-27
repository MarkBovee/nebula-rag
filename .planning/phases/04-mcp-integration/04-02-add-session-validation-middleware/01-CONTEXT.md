# 04-02: Add Session Validation Middleware - Context

## Current State
- Phase 3: Service Layer completed with business logic enforcement
- Phase 4: 04-01 created MCP tool handlers (not yet executed)
- Existing MCP infrastructure in NebulaRAG.Mcp project
- Session validation is critical for security before exposing plan management to agents

## Dependencies
- Requires PlanService and TaskService from Phase 3
- Depends on 04-01 MCP tool handlers being implemented
- Must integrate with existing McpTransportHandler

## Technical Approach
- Create SessionValidator middleware that intercepts MCP requests
- Validate session ownership before executing any plan operation
- Extend PlanException for session validation errors
- Integrate with existing MCP transport handler pattern

## Key Considerations
- Session validation must be performed early in the request pipeline
- Should handle both plan creation (no existing plan) and plan access (existing plan)
- Error handling must be consistent with existing patterns
- Must not break existing MCP infrastructure