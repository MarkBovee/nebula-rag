# Testing Patterns

**Analysis Date:** 2026-02-27

## Test Framework

**Runner:**
- Playwright ^1.51.1 for dashboard visual testing
- MSTest for C# unit tests
- No Jest or Vitest configuration found

**Assertion Library:**
- Playwright built-in `expect()` for dashboard tests
- MSTest `Assert` class for C# tests

**Run Commands:**
```bash
# Dashboard visual tests
npm run test:visual           # Run all visual tests
npm run test:visual:update    # Update test snapshots
npm run test:visual:ui        # Run tests in UI mode
npm run test:visual:loop      # Run specific test repeatedly

# C# unit tests
dotnet test                   # Run all tests
```

## Test File Organization

**Location:**
- Dashboard tests: `./dashboard/tests/visual/`
- C# unit tests: `./tests/NebulaRAG.Tests/`
- Co-located with source files (no separate test directories)

**Naming:**
- Dashboard: `*.spec.ts` (e.g., `dashboard.visual.spec.ts`)
- C#: `*.cs` with class name `UnitTest1` (placeholder naming)

**Structure:**
```
dashboard/
├── tests/
│   └── visual/
│       ├── dashboard.visual.spec.ts     # Visual regression tests
│       ├── dashboard.data.spec.ts       # Functional component tests
│       └── dashboard.data.spec.ts-snapshots/  # Test screenshots
```

## Test Structure

**Suite Organization:**
```typescript
// Playwright test with fixtures
import { expect, test } from '@playwright/test';

// Test fixtures in beforeEach
test.beforeEach(async ({ page }) => {
  await installDeterministicRuntime(page);
  await mockDashboardApi(page);
  await page.goto('/');
  await page.waitForLoadState('networkidle');
});

// Individual test cases
test('dashboard overview visual baseline', async ({ page }) => {
  await expect(page).toHaveScreenshot('dashboard-overview.png', {
    fullPage: true,
    animations: 'disabled',
  });
});
```

**Patterns:**
- `test.beforeEach()` for setup across all tests
- Test isolation with fresh page instances
- Deterministic runtime for reproducible visual tests
- Mock API responses for test independence

## Mocking

**Framework:** Playwright route mocking

**Patterns:**
```typescript
// API endpoint mocking
const mockDashboardApi = async (page: Page) => {
  await page.route('**/api/dashboard**', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(dashboardFixture),
    });
  });

  await page.route('**/api/query', async (route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(queryResponseFixture),
    });
  });
};

// Deterministic runtime for visual tests
const installDeterministicRuntime = async (page: Page) => {
  await page.addInitScript(() => {
    const fixedTimestamp = new Date('2026-01-01T12:00:00.000Z').valueOf();

    class FixedDate extends Date {
      constructor(...args: ConstructorParameters<typeof Date>) {
        if (args.length === 0) {
          super(fixedTimestamp);
          return;
        }
        super(...args);
      }
      static now() { return fixedTimestamp; }
    }

    window.Date = FixedDate;
    Math.random = () => 0.42;
  });
};
```

**What to Mock:**
- API responses for dashboard data
- Time-based functions for deterministic tests
- Random number generation for consistent snapshots

**What NOT to Mock:**
- DOM rendering behavior
- Core Playwright functionality
- Actual business logic implementation

## Fixtures and Factories

**Test Data:**
```typescript
// Complex fixtures for dashboard testing
const dashboardFixture = {
  health: {
    isHealthy: true,
    message: 'Nebula index healthy',
  },
  stats: {
    documentCount: 124,
    chunkCount: 1822,
    totalTokens: 502211,
    // ... more test data
  },
  sources: [
    // array of source objects
  ],
  memoryStats: {
    // memory analytics data
  }
};

// Query response fixture
const queryResponseFixture = {
  query: 'nebula',
  limit: 8,
  matches: [
    // array of search results
  ],
};
```

**Location:**
- Test data defined inline in test files
- No separate test data directory
- Reusable fixtures within test file scope

## Coverage

**Requirements:** No coverage tooling detected
- Visual testing focuses on UI consistency
- Unit tests cover core functionality
- No code coverage metrics enforced

**View Coverage:**
```bash
# No coverage command configured
```

## Test Types

**Unit Tests:**
- Scope: Core functionality (chunking, embeddings)
- Location: `./tests/NebulaRAG.Tests/`
- Framework: MSTest with xUnit-style attributes
- Example: Text chunking and embedding generation

**Visual Tests:**
- Scope: Dashboard UI components and layouts
- Location: `./dashboard/tests/visual/`
- Framework: Playwright
- Features:
  - Screenshot-based regression testing
  - Multi-tab navigation testing
  - Mobile responsiveness validation
  - Interactive component behavior

**Functional Tests:**
- Scope: Dashboard data rendering and API interactions
- Location: `./dashboard/tests/visual/dashboard.data.spec.ts`
- Features:
  - Data validation in different views
  - Search functionality
  - Navigation between tabs
  - Mobile layout testing

## Common Patterns

**Async Testing:**
```typescript
// Async/await pattern for page interactions
test('executes a search query and renders returned matches', async ({ page }) => {
  await page.getByTestId('tab-search').click();
  await page.getByTestId('search-input').fill('nebula');
  await page.getByTestId('search-submit').click();

  await expect(page.getByTestId('search-results')).toContainText('2 results found');
  await expect(page.getByTestId('search-result-item')).toHaveCount(2);
});
```

**Error Testing:**
```typescript
// Error boundary and client error reporting
test('handles API errors gracefully', async ({ page }) => {
  await page.route('**/api/health**', async (route) => {
    await route.fulfill({ status: 500 });
  });

  await expect(page.getByText('Error loading dashboard')).toBeVisible();
});
```

**Visual Testing:**
```typescript
// Screenshot comparison with options
await expect(page).toHaveScreenshot('dashboard-overview.png', {
  fullPage: true,
  animations: 'disabled',
  maxDiffPixels: 100,  // Allow minor differences
});
```

**Component Testing:**
```typescript
// Individual component testing
await expect(page.getByRole('heading', { name: 'Index Health' }).locator('..'))
  .toHaveScreenshot('component-index-health.png', {
    animations: 'disabled',
  });
```

---

*Testing analysis: 2026-02-27*