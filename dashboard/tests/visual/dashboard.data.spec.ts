import { expect, test, type Page, type Route } from '@playwright/test';

const dashboardFixture = {
  health: {
    isHealthy: true,
    message: 'Nebula index healthy',
  },
  stats: {
    documentCount: 124,
    chunkCount: 1822,
    projectCount: 3,
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
  ],
  memoryStats: {
    totalMemories: 42,
    recent24HoursCount: 8,
    distinctSessionCount: 6,
    distinctProjectCount: 3,
    averageTagsPerMemory: 2.5,
    typeCounts: [
      { type: 'semantic', count: 20 },
      { type: 'episodic', count: 18 },
      { type: 'procedural', count: 4 },
    ],
    topTags: [
      { tag: 'project:NebulaRAG', count: 12 },
      { tag: 'decision', count: 8 },
      { tag: 'bug', count: 5 },
    ],
    dailyCounts: [
      { dateUtc: '2026-01-01T00:00:00.000Z', count: 8 },
      { dateUtc: '2026-01-02T00:00:00.000Z', count: 10 },
      { dateUtc: '2026-01-03T00:00:00.000Z', count: 6 },
    ],
    recentSessions: [
      { sessionId: 'session-a', memoryCount: 12, lastMemoryAtUtc: '2026-01-01T09:20:00.000Z' },
      { sessionId: 'session-b', memoryCount: 7, lastMemoryAtUtc: '2026-01-01T10:20:00.000Z' },
    ],
  },
  activity: [
    {
      timestampUtc: '2026-01-01T10:25:00.000Z',
      timestamp: '2026-01-01T10:25:00.000Z',
      eventType: 'index',
      description: 'Indexed E:/Projects/NebulaRAG/README.md',
    },
    {
      timestampUtc: '2026-01-01T10:20:00.000Z',
      timestamp: '2026-01-01T10:20:00.000Z',
      eventType: 'query',
      description: 'Searched for "nebula"',
    },
  ],
  performanceMetrics: [
    {
      timestampUtc: '2026-01-01T09:00:00.000Z',
      queryLatencyMs: 28.6,
      indexingRateDocsPerSec: 7.2,
      cpuUsagePercent: 22.4,
    },
    {
      timestampUtc: '2026-01-01T09:30:00.000Z',
      queryLatencyMs: 30.1,
      indexingRateDocsPerSec: 6.9,
      cpuUsagePercent: 24.1,
    },
  ],
  generatedAtUtc: '2026-01-01T10:31:00.000Z',
};

const queryResponseFixture = {
  query: 'nebula',
  limit: 8,
  matches: [
    {
      sourcePath: 'E:/Projects/NebulaRAG/README.md',
      chunkText: 'Nebula RAG is a lightweight PostgreSQL + pgvector retrieval system.',
      chunkIndex: 3,
      score: 0.95,
    },
    {
      sourcePath: 'E:/Projects/NebulaRAG/docs/ARCHITECTURE.md',
      chunkText: 'The architecture uses Core, Cli, Mcp and AddonHost modules.',
      chunkIndex: 9,
      score: 0.89,
    },
  ],
};

/// <summary>
/// Mocks API routes used by the dashboard so tests are deterministic.
/// </summary>
/// <param name="page">Playwright page instance.</param>
/// <returns>Promise that resolves when all API routes are mocked.</returns>
const mockDashboardApi = async (page: Page) => {
  await page.route('**/api/dashboard**', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(dashboardFixture),
    });
  });

  await page.route('**/api/memory/stats**', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(dashboardFixture.memoryStats),
    });
  });

  await page.route('**/api/query', async (route: Route) => {
    await route.fulfill({
      status: 200,
      contentType: 'application/json',
      body: JSON.stringify(queryResponseFixture),
    });
  });

  await page.route('**/api/index', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ ok: true }) });
  });

  await page.route('**/api/source/delete', async (route: Route) => {
    await route.fulfill({ status: 200, contentType: 'application/json', body: JSON.stringify({ deleted: true }) });
  });

  await page.route('**/api/client-errors', async (route: Route) => {
    await route.fulfill({ status: 202, contentType: 'application/json', body: '{}' });
  });
};

test.beforeEach(async ({ page }) => {
  await mockDashboardApi(page);
  await page.goto('/');
  await page.waitForLoadState('networkidle');
});

test('renders overview status and summary metrics from dashboard data', async ({ page }) => {
  await expect(page.getByTestId('status-system-value')).toHaveText('Healthy');
  await expect(page.getByTestId('status-documents-value')).toHaveText('124');
  await expect(page.getByTestId('status-chunks-value')).toHaveText('1,822');
  await expect(page.getByTestId('status-projects-value')).toHaveText('3');

  await expect(page.getByTestId('index-health-card')).toBeVisible();
  await expect(page.getByTestId('metric-total-documents')).toContainText('124');
  await expect(page.getByTestId('metric-total-chunks')).toContainText('1,822');
  await expect(page.getByTestId('source-breakdown-list')).toBeVisible();
  await expect(page.getByTestId('source-breakdown-item')).toHaveCount(3);
});

test('navigates through all tabs and validates key data surfaces', async ({ page }) => {
  await page.getByTestId('tab-search').click();
  await expect(page.getByTestId('panel-search')).toBeVisible();
  await expect(page.getByTestId('search-card')).toContainText('Search and Query');

  await page.getByTestId('tab-sources').click();
  await expect(page.getByTestId('panel-sources')).toBeVisible();
  await expect(page.getByTestId('source-row')).toHaveCount(3);

  await page.getByTestId('tab-activity').click();
  await expect(page.getByTestId('panel-activity')).toBeVisible();
  await expect(page.getByTestId('activity-item')).toHaveCount(2);

  await page.getByTestId('tab-performance').click();
  await expect(page.getByTestId('panel-performance')).toBeVisible();
  await expect(page.getByTestId('performance-card')).toContainText('Performance Timeline (24h)');

  await page.getByTestId('tab-memory').click();
  await expect(page.getByTestId('panel-memory')).toBeVisible();
  await expect(page.getByTestId('memory-total-memories')).toContainText('42');
  await expect(page.getByTestId('memory-recent-24h')).toContainText('8');
  await expect(page.getByTestId('memory-distinct-sessions')).toContainText('6');
  await expect(page.getByTestId('memory-distinct-projects')).toContainText('3');
});

test('executes a search query and renders returned matches', async ({ page }) => {
  await page.getByTestId('tab-search').click();
  await page.getByTestId('search-input').fill('nebula');
  await page.getByTestId('search-submit').click();

  await expect(page.getByTestId('search-results')).toContainText('2 results found');
  await expect(page.getByTestId('search-result-item')).toHaveCount(2);
  await expect(page.getByTestId('search-results')).toContainText('95.0%');
});

test('uses mobile layout for tabs and search controls without horizontal overflow', async ({ page }) => {
  await page.setViewportSize({ width: 390, height: 844 });
  await page.reload();
  await page.waitForLoadState('networkidle');

  const isOverflowing = await page.evaluate(() => {
    const root = document.documentElement;
    return root.scrollWidth > root.clientWidth;
  });

  expect(isOverflowing).toBe(false);

  await page.getByTestId('tab-search').click();
  const inputBox = await page.getByTestId('search-input').boundingBox();
  const buttonBox = await page.getByTestId('search-submit').boundingBox();

  expect(inputBox).not.toBeNull();
  expect(buttonBox).not.toBeNull();

  if (inputBox && buttonBox) {
    expect(buttonBox.y).toBeGreaterThan(inputBox.y);
  }
});
