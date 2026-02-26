/// <summary>
/// Type definitions for Nebula RAG dashboard API responses.
/// </summary>

/// <summary>
/// Health check response from the RAG system.
/// </summary>
export interface HealthResponse {
  isHealthy: boolean;
  message: string;
}

/// <summary>
/// Index statistics including document count, chunk count, and storage info.
/// </summary>
export interface IndexStats {
  documentCount: number;
  chunkCount: number;
  projectCount?: number;
  totalTokens: number;
  oldestIndexedAt?: string | null;
  newestIndexedAt?: string | null;

  // Legacy fields kept optional for compatibility with older payloads.
  totalDocuments?: number;
  totalChunks?: number;
  averageChunkSize?: number;
  vectorDimensions?: number;
  lastUpdated?: string;
  indexSizeBytes?: number;
}

/// <summary>
/// Information about a single source in the index.
/// </summary>
export interface SourceInfo {
  sourcePath: string;
  projectId?: string;
  chunkCount: number;
  indexedAt: string;
  contentHash?: string;

  // Legacy fields kept optional for compatibility with older payloads.
  documentCount?: number;
  lastIndexedAt?: string;
}

/// <summary>
/// Query result from the RAG system.
/// </summary>
export interface QueryResult {
  query: string;
  limit: number;
  matches: RagSearchResult[];
  elapsedMilliseconds?: number;
}

/// <summary>
/// A single search result with context snippet.
/// </summary>
export interface RagSearchResult {
  sourcePath: string;
  chunkIndex: number;
  chunkText: string;
  score: number;
}

/// <summary>
/// Activity log entry for real-time event monitoring.
/// </summary>
export interface ActivityEvent {
  timestampUtc?: string;
  timestamp: string;
  eventType: 'index' | 'query' | 'delete' | 'error' | 'mcp' | 'system';
  description: string;
  metadata?: Record<string, any>;
}

/// <summary>
/// Client-side browser error payload sent to the backend for diagnostics.
/// </summary>
export interface ClientErrorReport {
  message: string;
  stack?: string;
  source: 'window.error' | 'unhandledrejection';
  url: string;
  userAgent: string;
  severity: 'error' | 'warning';
  timestamp: string;
}

/// <summary>
/// Aggregated dashboard payload returned by /api/dashboard.
/// </summary>
export interface DashboardSnapshot {
  health: HealthResponse;
  stats: IndexStats;
  sources: SourceInfo[];
  memoryStats: MemoryDashboardStats;
  activity: ActivityEvent[];
  performanceMetrics: PerformanceMetric[];
  generatedAtUtc: string;
}

/// <summary>
/// Aggregated memory analytics for dashboard visualizations.
/// </summary>
export type MemoryScopeType = 'global' | 'project' | 'session';

/// <summary>
/// Aggregated memory analytics for dashboard visualizations.
/// </summary>
export interface MemoryDashboardStats {
  totalMemories: number;
  recent24HoursCount: number;
  distinctSessionCount: number;
  distinctProjectCount: number;
  averageTagsPerMemory: number;
  firstMemoryAtUtc?: string | null;
  lastMemoryAtUtc?: string | null;
  typeCounts: MemoryTypeCount[];
  topTags: MemoryTagCount[];
  dailyCounts: MemoryDailyCount[];
  recentSessions: MemorySessionSummary[];
}

/// <summary>
/// Count summary for one memory type.
/// </summary>
export interface MemoryTypeCount {
  type: string;
  count: number;
}

/// <summary>
/// Count summary for one memory tag.
/// </summary>
export interface MemoryTagCount {
  tag: string;
  count: number;
}

/// <summary>
/// Count summary for one UTC day.
/// </summary>
export interface MemoryDailyCount {
  dateUtc: string;
  count: number;
}

/// <summary>
/// Session-level memory summary.
/// </summary>
export interface MemorySessionSummary {
  sessionId: string;
  memoryCount: number;
  lastMemoryAtUtc: string;
}

/// <summary>
/// Performance metrics over a time range.
/// </summary>
export interface PerformanceMetric {
  timestampUtc: string;
  queryLatencyMs: number;
  indexingRateDocsPerSec: number;
  cpuUsagePercent: number;
  memoryUsageMb?: number;
}

/// <summary>
/// Dashboard state combining all metrics.
/// </summary>
export interface DashboardState {
  health: HealthResponse;
  stats: IndexStats;
  sources: SourceInfo[];
  memoryStats: MemoryDashboardStats;
  recentActivity: ActivityEvent[];
  performanceMetrics: PerformanceMetric[];
  loading: boolean;
  error?: string;
}
