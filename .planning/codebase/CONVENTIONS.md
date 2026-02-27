# Coding Conventions

**Analysis Date:** 2026-02-27

## Naming Patterns

**Files:**
- PascalCase for C# files and class names (e.g., `RagSearchResult.cs`, `TextChunker.cs`)
- kebab-case for component and utility files in dashboard (e.g., `project-grouping.ts`, `theme.ts`)
- .ts extension for TypeScript files, .cs for C# files

**Functions:**
- PascalCase for C# public methods and properties (e.g., `ExtractProjectName`, `GetProjectNameForSource`)
- camelCase for TypeScript/JavaScript methods (e.g., `resolvePathBase`, `mockDashboardApi`)
- Private methods use underscore prefix in TypeScript: `_privateHelper()`

**Variables:**
- PascalCase for C# public properties
- camelCase for all JavaScript/TypeScript variables (e.g., `activityLog`, `dashboardFixture`)
- Descriptive names: `resolvePathBase` instead of `getPath`
- Avoid short names except in small scopes

**Types:**
- PascalCase for C# types and interfaces (e.g., `RagSearchResult`, `ProjectSourceSummary`)
- camelCase for TypeScript interfaces and types when exported
- I prefix for C# interfaces (e.g., `IEmbeddingGenerator`)

## Code Style

**Formatting:**
- TypeScript: Uses strict ESLint configuration via tsconfig.json with:
  - `"strict": true`
  - `"noUnusedLocals": true`
  - `"noUnusedParameters": true`
  - `"noFallthroughCasesInSwitch": true`
- C#: Default Visual Studio formatting with .editorconfig
- 2-space indentation in TypeScript, tabs in C#
- Single quotes for strings in TypeScript unless escape sequences present
- Trailing commas in multi-line object literals

**Linting:**
- No explicit ESLint/Prettier config found
- TypeScript compiler handles linting via tsconfig.json strict mode
- Playwright handles test linting

## Import Organization

**Order:**
```typescript
// 1. Standard library imports
import { expect, test } from '@playwright/test';

// 2. External dependencies
import axios, { AxiosInstance } from 'axios';

// 3. Internal/relative imports with @ alias
import type { HealthResponse } from '@/types';
import { NebularRagClient } from '@/api/client';
```

**Path Aliases:**
- `@/` points to `src/` directory
- Used consistently: `@/types`, `@/api/client`, `@/utils`

## Error Handling

**Patterns:**
```typescript
// Try/catch for async operations
try {
  await this.api.post('api/client-errors', errorReport);
  this.addActivity('error', `Client error reported: ${errorReport.message}`);
} catch {
  this.addActivity('error', `Client error report failed: ${errorReport.message}`);
}

// Axios error handling with interceptors
this.api.interceptors.response.use(
  (response) => response,
  (error) => {
    this.addActivity('error', `API error: ${error.message}`);
    return Promise.reject(error);
  }
);
```

**C# Patterns:**
- Custom exception types in `Exceptions` folder
- Sealed records for immutable data transfer objects
- XML documentation comments for all public APIs

## Logging

**Framework:** Custom activity logging in dashboard API client

**Patterns:**
```typescript
// Activity logging for all API operations
addActivity(eventType: 'index' | 'query' | 'delete' | 'error', description: string): void {
  this.activityLog.push({
    timestamp: new Date().toISOString(),
    eventType,
    description,
    metadata: { loggedAt: new Date().toLocaleTimeString() }
  });

  // Keep only last 100 entries
  if (this.activityLog.length > 100) {
    this.activityLog = this.activityLog.slice(-100);
  }
}
```

## Comments

**When to Comment:**
- Non-obvious logic blocks (e.g., path resolution logic)
- API endpoint handlers with complex behavior
- Complex business logic with multiple conditions
- All public methods with XML docstrings in C#

**JSDoc/TSDoc:**
```typescript
/// <summary>
/// Resolves the hosting base path from the current location.
/// Example: /nebula/dashboard/ -> /nebula, /dashboard/ -> empty string.
/// </summary>
/// <returns>Base path prefix that should be prepended to API routes.</returns>
const resolvePathBase = (): string => {
  // implementation
};
```

## Function Design

**Size:**
- Prefer functions under 20 lines
- Single level of abstraction per function
- Clear separation of concerns

**Parameters:**
- Maximum 3 parameters when possible
- Use request objects for complex parameters
- Optional parameters at end with defaults

**Return Values:**
- Consistent return types
- Promise-based for async operations
- Meaningful success/failure indicators

## Module Design

**Exports:**
- Named exports for classes and utilities
- Default exports only for main module entry points
- Barrel files for organizing related exports

**TypeScript Patterns:**
```typescript
// Class with private fields
export class NebularRagClient {
  private api: AxiosInstance;
  private activityLog: ActivityEvent[] = [];

  // Constructor with dependency injection
  constructor(baseUrl = '') {
    // implementation
  }

  // Async methods with proper typing
  async getHealth(): Promise<HealthResponse> {
    // implementation
  }
}

// Utility functions
export const extractProjectName = (sourcePath: string): string => {
  // implementation
};
```

---

*Convention analysis: 2026-02-27*