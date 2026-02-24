import { expect, test } from '@playwright/test';

const dashboardFixture = {
  health: {
    isHealthy: true,
    message: 'Nebula index healthy',
  },
  stats: {
    documentCount: 124,
    chunkCount: 1822,
    totalTokens: 502211,
    oldestIndexedAt: '2026-01-01T08:00:00.000Z',
    newestIndexedAt: '2026-01-01T10:30:00.000Z',
    indexSizeBytes: 8421376,
  },
  sources: [
    {
      sourcePath: 'E:/Projects/NebulaRAG/docs/ARCHITECTURE.md',
      chunkCount: 212,
      indexedAt: '2026-01-01T10:30:00.000Z',
      documentCount: 1,
    },
    {
      sourcePath: 'E:/Projects/NebulaRAG/src/NebulaRAG.Core/Services/RagQueryService.cs',
      chunkCount: 315,
      indexedAt: '2026-01-01T10:10:00.000Z',
      documentCount: 1,
    },
    {
      sourcePath: 'E:/Projects/NebulaRAG/README.md',
      chunkCount: 104,
      indexedAt: '2026-01-01T09:40:00.000Z',
      documentCount: 1,
    },
    {
      sourcePath: 'E:/Projects/NebulaRAG/docs/PRODUCTION_READINESS_PLAN.md',
      chunkCount: 433,
      indexedAt: '2026-01-01T09:10:00.000Z',
      documentCount: 1,
    },
  ],
  generatedAtUtc: '2026-01-01T10:31:00.000Z',
};

/// <summary>
/// Stabilizes browser timing and random values to keep visual snapshots deterministic.
/// </summary>
/// <param name="page">Playwright page instance.</param>
/// <returns>Promise that resolves when deterministic globals are installed.</returns>
const installDeterministicRuntime = async (page: Parameters<typeof test.beforeEach>[0]['page']) => {
  await page.addInitScript(() => {
    const fixedTimestamp = new Date('2026-01-01T12:00:00.000Z').valueOf();

    const OriginalDate = Date;
    class FixedDate extends OriginalDate {
      constructor(...args: ConstructorParameters<typeof Date>) {
        if (args.length === 0) {
          super(fixedTimestamp);
          return;
        }

        super(...args);
      }

      static now() {
        return fixedTimestamp;
      }
    }

    // @ts-expect-error runtime replacement for deterministic rendering
    window.Date = FixedDate;
    Math.random = () => 0.42;
  });
};

/// <summary>
/// Mocks dashboard API endpoints so visual tests do not depend on external services.
/// </summary>
/// <param name="page">Playwright page instance.</param>
/// <returns>Promise that resolves when all routes are mocked.</returns>
const mockDashboardApi = async (page: Parameters<typeof test.beforeEach>[0]['page']) => {
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
      body: JSON.stringify({
        query: 'nebula',
        limit: 8,
        results: [
          {
            sourcePath: 'E:/Projects/NebulaRAG/README.md',
            snippet: 'Nebula RAG is a lightweight PostgreSQL + pgvector retrieval system.',
            score: 0.95,
          },
          {
            sourcePath: 'E:/Projects/NebulaRAG/docs/ARCHITECTURE.md',
            snippet: 'The architecture uses Core, Cli, Mcp and AddonHost modules.',
            score: 0.89,
          },
        ],
      }),
    });
  });

  await page.route('**/api/index', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ok: true }) });
  });

  await page.route('**/api/source/delete', async (route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ deleted: true }) });
  });

  await page.route('**/api/client-errors', async (route) => {
    await route.fulfill({ status: 202, contentType: 'application/json', body: '{}' });
  });
};

test.beforeEach(async ({ page }) => {
  await installDeterministicRuntime(page);
  await mockDashboardApi(page);
  await page.goto('/');
  await page.waitForLoadState('networkidle');
});

test('dashboard overview visual baseline', async ({ page }) => {
  await expect(page).toHaveScreenshot('dashboard-overview.png', {
    fullPage: true,
    animations: 'disabled',
  });
});

test('dashboard tabs visual baseline', async ({ page }) => {
  const tabs = ['Overview', 'Search', 'Sources', 'Activity', 'Performance'];

  for (const tab of tabs) {
    await page.getByRole('button', { name: tab }).click();
    await expect(page.locator('main')).toHaveScreenshot(`dashboard-tab-${tab.toLowerCase()}.png`, {
      animations: 'disabled',
    });
  }
});

test('component cards visual baseline', async ({ page }) => {
  await page.getByRole('button', { name: 'Overview' }).click();
  await expect(page.getByRole('heading', { name: 'Index Health' }).locator('..')).toHaveScreenshot('component-index-health.png', {
    animations: 'disabled',
  });
  await expect(page.getByRole('heading', { name: 'Source Breakdown' }).locator('..')).toHaveScreenshot('component-source-breakdown.png', {
    animations: 'disabled',
  });
  await expect(page.getByRole('heading', { name: 'Performance Timeline (24h)' }).locator('..')).toHaveScreenshot('component-performance-timeline.png', {
    animations: 'disabled',
  });

  await page.getByRole('complementary').getByRole('button', { name: 'Search' }).click();
  await page.getByPlaceholder('Search your indexed documents...').fill('nebula');
  await page.getByRole('main').getByRole('button', { name: 'Search' }).click();
  await expect(page.getByRole('heading', { name: 'Search and Query' }).locator('..')).toHaveScreenshot('component-search-analytics.png', {
    animations: 'disabled',
  });

  await page.getByRole('button', { name: 'Sources' }).click();
  await expect(page.getByRole('heading', { name: 'Source Management' }).locator('..')).toHaveScreenshot('component-source-manager.png', {
    animations: 'disabled',
  });

  await page.getByRole('button', { name: 'Activity' }).click();
  await expect(page.getByRole('heading', { name: 'Realtime Activity' }).locator('..')).toHaveScreenshot('component-activity-feed.png', {
    animations: 'disabled',
  });
});
