import axios, { AxiosInstance } from 'axios';
import type { HealthResponse, IndexStats, SourceInfo, QueryResult, ActivityEvent, ClientErrorReport, DashboardSnapshot, MemoryDashboardStats, MemoryScopeType } from '@/types';

/// <summary>
/// Resolves the hosting base path from the current location.
/// Example: /nebula/dashboard/ -> /nebula, /dashboard/ -> empty string.
/// </summary>
/// <returns>Base path prefix that should be prepended to API routes.</returns>
const resolvePathBase = (): string => {
  const marker = '/dashboard';
  const pathname = window.location.pathname;
  const markerIndex = pathname.indexOf(marker);
  if (markerIndex <= 0) {
    return '';
  }

  return pathname.substring(0, markerIndex);
};

/// <summary>
/// API client for interacting with the Nebula RAG backend.
/// Handles health checks, statistics retrieval, source management, and query operations.
/// </summary>
export class NebularRagClient {
  private api: AxiosInstance;
  private activityLog: ActivityEvent[] = [];

  /// <summary>
  /// Initializes the API client with base configuration.
  /// </summary>
  /// <param name="baseUrl">Base URL for the API (defaults to VITE_API_BASE_URL or current origin).</param>
  constructor(baseUrl = '') {
    const explicitBaseUrl = (import.meta as any).env?.VITE_API_BASE_URL as string | undefined;
    const pathBase = resolvePathBase();
    const resolvedBaseUrl = baseUrl || explicitBaseUrl || `${window.location.origin}${pathBase}`;

    this.api = axios.create({
      baseURL: resolvedBaseUrl,
      timeout: 30000,
      headers: {
        'Content-Type': 'application/json'
      }
    });

    // Intercept responses and log activity
    this.api.interceptors.response.use(
      (response) => {
        this.addActivity('query', `API call: ${response.config.method?.toUpperCase()} ${response.config.url}`);
        return response;
      },
      (error) => {
        this.addActivity('error', `API error: ${error.message}`);
        return Promise.reject(error);
      }
    );
  }

  /// <summary>
  /// Fetches the current health status of the RAG system.
  /// </summary>
  /// <returns>Health status response.</returns>
  async getHealth(): Promise<HealthResponse> {
    const response = await this.api.get<HealthResponse>('api/health');
    return response.data;
  }

  /// <summary>
  /// Retrieves comprehensive index statistics.
  /// </summary>
  /// <returns>Index statistics including document and chunk counts.</returns>
  async getStats(): Promise<IndexStats> {
    const response = await this.api.get<IndexStats>('api/stats');
    return response.data;
  }

  /// <summary>
  /// Lists all indexed sources with optional limit.
  /// </summary>
  /// <param name="limit">Maximum number of sources to return (default 100).</param>
  /// <returns>Array of source information objects.</returns>
  async listSources(limit = 100): Promise<SourceInfo[]> {
    const response = await this.api.get<SourceInfo[]>('api/sources', {
      params: { limit }
    });
    return response.data;
  }

  /// <summary>
  /// Retrieves an aggregated dashboard snapshot in a single API call.
  /// </summary>
  /// <returns>Dashboard snapshot containing health, stats, and source list.</returns>
  async getDashboard(): Promise<DashboardSnapshot> {
    const response = await this.api.get<DashboardSnapshot>('api/dashboard');

    return response.data;
  }

  /// <summary>
  /// Retrieves aggregated memory analytics from the backend.
  /// </summary>
  /// <returns>Memory analytics payload.</returns>
  async getMemoryStats(scope: MemoryScopeType = 'global', scopeValue?: string): Promise<MemoryDashboardStats> {
    const params: Record<string, string> = { scope };

    if (scope === 'project' && scopeValue) {
      params.projectId = scopeValue;
    }

    if (scope === 'session' && scopeValue) {
      params.sessionId = scopeValue;
    }

    const response = await this.api.get<MemoryDashboardStats>('api/memory/stats', { params });
    return response.data;
  }

  /// <summary>
  /// Lists memories from the management API with optional scope and filters.
  /// </summary>
  /// <param name="scope">Scope selector: global, project, or session.</param>
  /// <param name="scopeValue">Optional project/session id tied to selected scope.</param>
  /// <param name="limit">Maximum rows returned by the backend.</param>
  /// <returns>Raw memory records.</returns>
  async listMemories(scope: MemoryScopeType = 'global', scopeValue?: string, limit = 100): Promise<any[]> {
    const params: Record<string, any> = { scope, limit };
    if (scope === 'project' && scopeValue) {
      params.projectId = scopeValue;
    }

    if (scope === 'session' && scopeValue) {
      params.sessionId = scopeValue;
    }

    const response = await this.api.get<any[]>('api/memory/list', { params });
    return response.data;
  }

  /// <summary>
  /// Executes semantic memory search with optional scope filtering.
  /// </summary>
  /// <param name="text">Natural language memory search text.</param>
  /// <param name="scope">Scope selector: global, project, or session.</param>
  /// <param name="scopeValue">Optional project/session id tied to selected scope.</param>
  /// <param name="limit">Maximum number of ranked rows to return.</param>
  /// <returns>Semantic memory matches.</returns>
  async searchMemories(text: string, scope: MemoryScopeType = 'global', scopeValue?: string, limit = 20): Promise<any[]> {
    const payload: Record<string, any> = { text, scope, limit };

    if (scope === 'project' && scopeValue) {
      payload.projectId = scopeValue;
    }

    if (scope === 'session' && scopeValue) {
      payload.sessionId = scopeValue;
    }

    const response = await this.api.post<any[]>('api/memory/search', payload);
    return response.data;
  }

  /// <summary>
  /// Executes a query against the indexed documents.
  /// </summary>
  /// <param name="text">Query text to search for.</param>
  /// <param name="limit">Maximum number of results to return.</param>
  /// <returns>Query results with matching documents and snippets.</returns>
  async query(text: string, limit = 5): Promise<QueryResult> {
    const response = await this.api.post<QueryResult>('api/query', {
      text,
      limit
    });
    this.addActivity('query', `Searched: "${text}" (${response.data.matches.length} results)`);
    return response.data;
  }

  /// <summary>
  /// Indexes documents from the specified source path.
  /// </summary>
  /// <param name="sourcePath">File path or directory to index.</param>
  /// <returns>Index summary with document and chunk counts.</returns>
  async indexSource(sourcePath: string): Promise<any> {
    const response = await this.api.post('api/index', {
      sourcePath
    });
    this.addActivity('index', `Indexed: ${sourcePath}`);
    return response.data;
  }

  /// <summary>
  /// Deletes all documents from a specific source.
  /// </summary>
  /// <param name="sourcePath">Source path to delete.</param>
  /// <returns>Success status.</returns>
  async deleteSource(sourcePath: string): Promise<{ deleted: boolean }> {
    const response = await this.api.post<{ deleted: boolean }>('api/source/delete', {
      sourcePath
    });
    this.addActivity('delete', `Deleted source: ${sourcePath}`);
    return response.data;
  }

  /// <summary>
  /// Reports a browser runtime error to the backend diagnostics endpoint.
  /// </summary>
  /// <param name="errorReport">Client-side error payload.</param>
  /// <returns>Promise that resolves once the error has been forwarded.</returns>
  async reportClientError(errorReport: ClientErrorReport): Promise<void> {
    try {
      await this.api.post('api/client-errors', errorReport);
      this.addActivity('error', `Client error reported: ${errorReport.message}`);
    } catch {
      this.addActivity('error', `Client error report failed: ${errorReport.message}`);
    }
  }

  /// <summary>
  /// Gets the activity log of all API operations and events.
  /// </summary>
  /// <returns>Array of activity events in chronological order.</returns>
  getActivityLog(): ActivityEvent[] {
    return [...this.activityLog].reverse(); // Most recent first
  }

  /// <summary>
  /// Internal helper to add an activity event to the log.
  /// </summary>
  /// <param name="eventType">Type of event (index, query, delete, error).</param>
  /// <param name="description">Human-readable description of the event.</param>
  private addActivity(eventType: 'index' | 'query' | 'delete' | 'error', description: string): void {
    this.activityLog.push({
      timestamp: new Date().toISOString(),
      eventType,
      description,
      metadata: {
        loggedAt: new Date().toLocaleTimeString()
      }
    });

    // Keep only last 100 entries in memory
    if (this.activityLog.length > 100) {
      this.activityLog = this.activityLog.slice(-100);
    }
  }
}

/// <summary>
/// Global API client instance for use throughout the dashboard.
/// </summary>
export const apiClient = new NebularRagClient();
