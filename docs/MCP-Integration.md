# MCP Integration for Plan Management

## Overview

NebulaRAG provides comprehensive MCP (Model Context Protocol) integration for plan lifecycle management. This allows AI agents to create, manage, and track plans with full session validation and security enforcement.

## Available Tools

### create_plan

Creates a new plan with initial tasks.

**Parameters:**
- `sessionId`: Session ID for the plan (required)
- `planName`: Name of the new plan (required)
- `projectId`: Project ID for the plan (required)
- `initialTasks`: Array of initial tasks for the plan (optional)

**Example:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "create_plan",
    "arguments": {
      "sessionId": "agent-session-1",
      "planName": "Project Planning",
      "projectId": "project-123",
      "initialTasks": ["Research requirements", "Design architecture", "Implement features"]
    }
  }
}
```

### get_plan

Retrieves a specific plan by ID.

**Parameters:**
- `sessionId`: Session ID for the plan (required)
- `planId`: ID of the plan to retrieve (required)

**Example:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "get_plan",
    "arguments": {
      "sessionId": "agent-session-1",
      "planId": "plan-123"
    }
  }
}
```

### list_plans

Lists all plans for the current session.

**Parameters:**
- `sessionId`: Session ID to list plans for (required)

**Example:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "list_plans",
    "arguments": {
      "sessionId": "agent-session-1"
    }
  }
}
```

### update_plan

Updates plan details or status.

**Parameters:**
- `sessionId`: Session ID for the plan (required)
- `planId`: ID of the plan to update (required)
- `planName`: New name for the plan (optional)
- `status`: New status for the plan (optional)

**Example:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "update_plan",
    "arguments": {
      "sessionId": "agent-session-1",
      "planId": "plan-123",
      "planName": "Updated Project Planning",
      "status": "In Progress"
    }
  }
}
```

### complete_task

Completes a specific task.

**Parameters:**
- `sessionId`: Session ID for the plan (required)
- `planId`: ID of the plan containing the task (required)
- `taskId`: ID of the task to complete (required)

**Example:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "complete_task",
    "arguments": {
      "sessionId": "agent-session-1",
      "planId": "plan-123",
      "taskId": "task-456"
    }
  }
}
```

### update_task

Updates a specific task.

**Parameters:**
- `sessionId`: Session ID for the plan (required)
- `planId`: ID of the plan containing the task (required)
- `taskId`: ID of the task to update (required)
- `taskName`: New name for the task (optional)
- `status`: New status for the task (optional)

**Example:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "update_task",
    "arguments": {
      "sessionId": "agent-session-1",
      "planId": "plan-123",
      "taskId": "task-456",
      "taskName": "Updated Task Name",
      "status": "In Progress"
    }
  }
}
```

### archive_plan

Archives a plan.

**Parameters:**
- `sessionId`: Session ID for the plan (required)
- `planId`: ID of the plan to archive (required)

**Example:**
```json
{
  "method": "tools/call",
  "params": {
    "name": "archive_plan",
    "arguments": {
      "sessionId": "agent-session-1",
      "planId": "plan-123"
    }
  }
}
```

## Security Features

### Session Validation

All MCP tools enforce session ownership validation, ensuring that agents can only access plans belonging to their session. Session validation is performed before any plan operation, providing robust security and data integrity.

### One Active Plan Per Session

The system enforces a rule that only one active plan can exist per session, preventing conflicts and ensuring proper plan management.

### Error Handling

Comprehensive error handling is implemented for various scenarios:
- Missing or invalid session IDs
- Non-existent plans or tasks
- Invalid session access attempts
- Multiple active plans per session

## Testing

Comprehensive integration tests are available in the test suite:

- `PlanMcpToolTests.cs`: Tests for all MCP tool operations
- `SessionValidationTests.cs`: Tests for session validation logic

Run tests with:
```bash
dotnet test tests/NebulaRAG.Tests/
```

## Requirements Coverage

This MCP integration covers the following requirements:
- MCP-01: Create plan tool
- MCP-02: Get plan tool
- MCP-03: List plans tool
- MCP-04: Update plan tool
- MCP-05: Complete task tool
- MCP-06: Update task tool
- MCP-07: Archive plan tool
- ERROR-03: Session validation error handling

## Verification

All MCP tools have been verified to:
- Properly validate session ownership
- Handle errors appropriately
- Return correct JSON-RPC responses
- Enforce security constraints
- Comply with MCP specification