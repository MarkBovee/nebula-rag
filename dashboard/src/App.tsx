import React, { useState, useEffect } from 'react';
import { nebulaTheme, getBackgroundGradient } from '@/styles/theme';
import { apiClient } from '@/api/client';
import type { DashboardState, MemoryScopeType, MemoryDashboardStats } from '@/types';
import IndexHealth from '@/components/IndexHealth';
import SearchAnalytics from '@/components/SearchAnalytics';
import SourceBreakdown from '@/components/SourceBreakdown';
import ActivityFeed from '@/components/ActivityFeed';
import SourceManager from '@/components/SourceManager';
import PerfTimeline from '@/components/PerfTimeline';
import MemoryInsights from '@/components/MemoryInsights';

type DashboardTab = 'overview' | 'search' | 'sources' | 'activity' | 'performance' | 'memory';

const styles = {
  container: {
    background: getBackgroundGradient(),
    minHeight: '100vh',
    color: nebulaTheme.colors.textPrimary,
    fontFamily: nebulaTheme.typography.fontFamily,
    padding: nebulaTheme.spacing.xl,
  } as React.CSSProperties,
  shell: {
    maxWidth: '1400px',
    margin: '0 auto',
    gap: nebulaTheme.spacing.xl,
  } as React.CSSProperties,
  nav: {
    background: nebulaTheme.colors.surface,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.xl,
    padding: nebulaTheme.spacing.lg,
    boxShadow: nebulaTheme.shadow.lg,
    height: 'fit-content',
    position: 'sticky',
    top: nebulaTheme.spacing.xl,
  } as React.CSSProperties,
  navTitle: {
    fontFamily: "'Rajdhani', sans-serif",
    fontSize: nebulaTheme.typography.fontSize['2xl'],
    fontWeight: nebulaTheme.typography.fontWeight.bold,
    letterSpacing: '0.03em',
    marginBottom: nebulaTheme.spacing.xs,
  } as React.CSSProperties,
  navSubtitle: {
    color: nebulaTheme.colors.textMuted,
    fontSize: nebulaTheme.typography.fontSize.xs,
    textTransform: 'uppercase',
    letterSpacing: '0.12em',
    marginBottom: nebulaTheme.spacing.lg,
  } as React.CSSProperties,
  content: {
    minWidth: 0,
  } as React.CSSProperties,
  title: {
    fontFamily: "'Rajdhani', sans-serif",
    fontSize: '2.25rem',
    fontWeight: nebulaTheme.typography.fontWeight.bold,
    marginBottom: nebulaTheme.spacing.sm,
    letterSpacing: '0.02em',
  } as React.CSSProperties,
  subtitle: {
    fontSize: nebulaTheme.typography.fontSize.base,
    color: nebulaTheme.colors.textSecondary,
  } as React.CSSProperties,
  header: {
    marginBottom: nebulaTheme.spacing.xl,
    padding: `${nebulaTheme.spacing.xl} ${nebulaTheme.spacing.xl}`,
    borderRadius: nebulaTheme.borderRadius.xl,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    background: 'linear-gradient(150deg, rgba(24, 31, 45, 0.96) 0%, rgba(29, 38, 54, 0.96) 58%, rgba(36, 46, 66, 0.96) 100%)',
    boxShadow: nebulaTheme.shadow.base,
  } as React.CSSProperties,
  statusBar: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
    gap: nebulaTheme.spacing.md,
    marginBottom: nebulaTheme.spacing.xl,
  } as React.CSSProperties,
  statusItem: {
    padding: `${nebulaTheme.spacing.md} ${nebulaTheme.spacing.md}`,
    background: nebulaTheme.colors.surface,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.md,
    boxShadow: nebulaTheme.shadow.sm,
    minHeight: '74px',
  } as React.CSSProperties,
  healthDot: (isHealthy: boolean) => ({
    width: '10px',
    height: '10px',
    borderRadius: '50%',
    background: isHealthy ? nebulaTheme.colors.success : nebulaTheme.colors.error,
    boxShadow: isHealthy
      ? `0 0 10px ${nebulaTheme.colors.success}` 
      : `0 0 10px ${nebulaTheme.colors.error}`,
  } as React.CSSProperties),
  statusLabel: {
    color: nebulaTheme.colors.textMuted,
    textTransform: 'uppercase',
    fontSize: nebulaTheme.typography.fontSize.xs,
    letterSpacing: '0.08em',
    marginBottom: nebulaTheme.spacing.xs,
  } as React.CSSProperties,
  statusValue: {
    fontSize: nebulaTheme.typography.fontSize.xl,
    fontWeight: nebulaTheme.typography.fontWeight.bold,
    fontFamily: "'Rajdhani', sans-serif",
  } as React.CSSProperties,
  gridContainer: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(360px, 1fr))',
    gap: nebulaTheme.spacing.lg,
    marginBottom: nebulaTheme.spacing['2xl'],
  } as React.CSSProperties,
  overviewGridContainer: {
    display: 'grid',
    gap: nebulaTheme.spacing.lg,
    marginBottom: nebulaTheme.spacing['2xl'],
  } as React.CSSProperties,
  fullWidth: {
    gridColumn: '1 / -1',
  } as React.CSSProperties,
  tabBar: {
    display: 'flex',
    flexDirection: 'column',
    gap: nebulaTheme.spacing.sm,
  } as React.CSSProperties,
  tabButton: (active: boolean) => ({
    textAlign: 'left',
    padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
    borderRadius: nebulaTheme.borderRadius.md,
    border: `1px solid ${active ? nebulaTheme.colors.accentPrimary : nebulaTheme.colors.surfaceBorder}`,
    background: active ? 'linear-gradient(120deg, rgba(253, 93, 50, 0.18), rgba(247, 183, 49, 0.18))' : nebulaTheme.colors.surfaceLight,
    color: active ? nebulaTheme.colors.textPrimary : nebulaTheme.colors.textSecondary,
    fontWeight: active ? nebulaTheme.typography.fontWeight.semibold : nebulaTheme.typography.fontWeight.medium,
    cursor: 'pointer',
    transition: nebulaTheme.transition.base,
    boxShadow: active ? nebulaTheme.shadow.glow.magenta : 'none',
  } as React.CSSProperties),
  refreshButton: {
    marginTop: nebulaTheme.spacing.lg,
    width: '100%',
    padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
    background: 'linear-gradient(120deg, rgba(253, 93, 50, 0.2), rgba(247, 183, 49, 0.2))',
    border: `1px solid ${nebulaTheme.colors.accentPrimary}`,
    color: nebulaTheme.colors.textPrimary,
    cursor: 'pointer',
    borderRadius: nebulaTheme.borderRadius.md,
    fontWeight: nebulaTheme.typography.fontWeight.semibold,
    transition: nebulaTheme.transition.base,
  } as React.CSSProperties,
  memoryScopeBar: {
    display: 'grid',
    gridTemplateColumns: 'minmax(160px, 220px) minmax(240px, 1fr) auto',
    gap: nebulaTheme.spacing.sm,
    marginBottom: nebulaTheme.spacing.lg,
    alignItems: 'center',
  } as React.CSSProperties,
  memoryScopeInput: {
    width: '100%',
    borderRadius: nebulaTheme.borderRadius.md,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    background: nebulaTheme.colors.surfaceLight,
    color: nebulaTheme.colors.textPrimary,
    padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
    fontSize: nebulaTheme.typography.fontSize.sm,
  } as React.CSSProperties,
  memoryScopeButton: {
    borderRadius: nebulaTheme.borderRadius.md,
    border: `1px solid ${nebulaTheme.colors.accentPrimary}`,
    background: 'linear-gradient(120deg, rgba(253, 93, 50, 0.18), rgba(247, 183, 49, 0.18))',
    color: nebulaTheme.colors.textPrimary,
    padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
    cursor: 'pointer',
    fontWeight: nebulaTheme.typography.fontWeight.semibold,
    minHeight: '38px',
  } as React.CSSProperties,
  memoryScopeHint: {
    color: nebulaTheme.colors.textMuted,
    fontSize: nebulaTheme.typography.fontSize.xs,
    letterSpacing: '0.03em',
    marginTop: `-${nebulaTheme.spacing.xs}`,
    marginBottom: nebulaTheme.spacing.md,
  } as React.CSSProperties,
  memoryScopeStatusRow: {
    display: 'flex',
    alignItems: 'center',
    gap: nebulaTheme.spacing.sm,
    marginTop: `-${nebulaTheme.spacing.xs}`,
    marginBottom: nebulaTheme.spacing.md,
    flexWrap: 'wrap',
  } as React.CSSProperties,
  memoryScopeStatusLabel: {
    color: nebulaTheme.colors.textMuted,
    fontSize: nebulaTheme.typography.fontSize.xs,
    textTransform: 'uppercase',
    letterSpacing: '0.08em',
  } as React.CSSProperties,
  memoryScopeBadge: (scope: MemoryScopeType) => ({
    display: 'inline-flex',
    alignItems: 'center',
    borderRadius: '999px',
    padding: `${nebulaTheme.spacing.xs} ${nebulaTheme.spacing.sm}`,
    fontSize: nebulaTheme.typography.fontSize.xs,
    fontWeight: nebulaTheme.typography.fontWeight.semibold,
    border: `1px solid ${scope === 'global' ? nebulaTheme.colors.success : nebulaTheme.colors.accentPrimary}`,
    background: scope === 'global'
      ? 'rgba(46, 213, 115, 0.16)'
      : 'linear-gradient(120deg, rgba(253, 93, 50, 0.2), rgba(247, 183, 49, 0.2))',
    color: nebulaTheme.colors.textPrimary,
    letterSpacing: '0.02em',
  } as React.CSSProperties),
};

const tabs: Array<{ key: DashboardTab; label: string }> = [
  { key: 'overview', label: 'Overview' },
  { key: 'search', label: 'Search' },
  { key: 'sources', label: 'Sources' },
  { key: 'activity', label: 'Activity' },
  { key: 'performance', label: 'Performance' },
  { key: 'memory', label: 'Memory' },
];

/// <summary>
/// Main App component for the Nebula RAG management dashboard.
/// Fetches and displays all index metrics, search analytics, source information, and activity logs.
/// </summary>
const App: React.FC = () => {
  const [dashboard, setDashboard] = useState<Partial<DashboardState>>({
    loading: true,
  });
  const [activeTab, setActiveTab] = useState<DashboardTab>('overview');
  const [memoryScope, setMemoryScope] = useState<MemoryScopeType>('global');
  const [memoryScopeValue, setMemoryScopeValue] = useState('');
  const [appliedMemoryScope, setAppliedMemoryScope] = useState<MemoryScopeType>('global');
  const [appliedMemoryScopeValue, setAppliedMemoryScopeValue] = useState('');

  const loadScopedMemoryStats = async (scope: MemoryScopeType, scopeValue: string, fallbackStats?: MemoryDashboardStats): Promise<MemoryDashboardStats | undefined> => {
    if (scope === 'global') {
      return fallbackStats;
    }

    const normalizedScopeValue = scopeValue.trim();
    if (!normalizedScopeValue) {
      throw new Error(scope === 'project' ? 'Project id is required for project scope.' : 'Session id is required for session scope.');
    }

    return apiClient.getMemoryStats(scope, normalizedScopeValue);
  };

  /// <summary>
  /// Refreshes all dashboard data by fetching from the API.
  /// Handles errors gracefully and updates the dashboard state.
  /// </summary>
  const refreshDashboard = async () => {
    setDashboard(prev => ({ ...prev, loading: true, error: undefined }));
    try {
      const snapshot = await apiClient.getDashboard();
      const scopedMemoryStats = await loadScopedMemoryStats(appliedMemoryScope, appliedMemoryScopeValue, snapshot.memoryStats);

      setDashboard({
        health: snapshot.health,
        stats: snapshot.stats,
        sources: snapshot.sources,
        memoryStats: scopedMemoryStats,
        performanceMetrics: snapshot.performanceMetrics,
        recentActivity: snapshot.activity ?? apiClient.getActivityLog(),
        loading: false,
      });
    } catch (error: any) {
      console.error('Dashboard refresh error:', error);
      setDashboard(prev => ({
        ...prev,
        loading: false,
        error: error?.message || 'Failed to load dashboard data',
      }));
    }
  };

  const applyMemoryScope = async () => {
    try {
      const normalizedScopeValue = memoryScopeValue.trim();
      if (memoryScope !== 'global' && !normalizedScopeValue) {
        throw new Error(memoryScope === 'project' ? 'Project id is required for project scope.' : 'Session id is required for session scope.');
      }

      setDashboard(prev => ({ ...prev, loading: true, error: undefined }));
      const scopedMemoryStats = await loadScopedMemoryStats(memoryScope, normalizedScopeValue, dashboard.memoryStats);
      setDashboard(prev => ({
        ...prev,
        memoryStats: scopedMemoryStats,
        loading: false,
      }));
      setAppliedMemoryScope(memoryScope);
      setAppliedMemoryScopeValue(normalizedScopeValue);
    } catch (error: any) {
      setDashboard(prev => ({
        ...prev,
        loading: false,
        error: error?.message || 'Failed to apply memory scope',
      }));
    }
  };

  /// <summary>
  /// Fetch dashboard data on mount and refresh on a 30-second interval while visible.
  /// </summary>
  useEffect(() => {
    refreshDashboard();

    const handleVisibilityChange = () => {
      if (document.visibilityState === 'visible') {
        refreshDashboard();
      }
    };

    const interval = setInterval(() => {
      if (document.visibilityState === 'visible') {
        refreshDashboard();
      }
    }, 30000);

    document.addEventListener('visibilitychange', handleVisibilityChange);

    return () => {
      clearInterval(interval);
      document.removeEventListener('visibilitychange', handleVisibilityChange);
    };
  }, []);

  const { health, stats, sources, memoryStats, recentActivity, performanceMetrics, loading, error } = dashboard;

  return (
    <div style={styles.container} className="nb-app" data-testid="dashboard-root">
      {/* Header */}
      <div style={styles.shell} className="nb-shell">
        <aside style={styles.nav} className="nb-nav nb-fade-up" data-testid="dashboard-nav">
          <h1 style={styles.navTitle}>Nebula RAG</h1>
          <p style={styles.navSubtitle}>Control Center</p>

          <div style={styles.tabBar} className="nb-tabbar">
            {tabs.map((tab) => (
              <button
                key={tab.key}
                onClick={() => setActiveTab(tab.key)}
                style={styles.tabButton(activeTab === tab.key)}
                aria-pressed={activeTab === tab.key}
                className="nb-tab-button"
                data-testid={`tab-${tab.key}`}
              >
                {tab.label}
              </button>
            ))}
          </div>

          <button onClick={refreshDashboard} disabled={loading} style={styles.refreshButton} data-testid="refresh-dashboard-button">
            {loading ? 'Refreshing...' : 'Refresh Data'}
          </button>
        </aside>

        <main style={styles.content} className="nb-content" data-testid="dashboard-content">
          <div style={styles.header} className="nb-fade-up">
            <h2 style={styles.title}>Operations Dashboard</h2>
            <p style={styles.subtitle}>Observe index health, query quality, source coverage, and runtime behavior in one place.</p>
          </div>

          {error && (
            <div style={{
              ...styles.statusItem,
              background: 'rgba(231, 76, 60, 0.15)',
              borderColor: nebulaTheme.colors.error,
              marginBottom: nebulaTheme.spacing.lg,
            }}>
              <span style={{ color: nebulaTheme.colors.error }}>Error: {error}</span>
            </div>
          )}

          {health && (
            <div style={styles.statusBar} className="nb-fade-up">
              <div style={styles.statusItem} className="nb-status-item" data-testid="status-system">
                <p style={styles.statusLabel}>System Status</p>
                <div style={{ display: 'flex', alignItems: 'center', gap: nebulaTheme.spacing.sm }}>
                  <div style={styles.healthDot(health.isHealthy)} />
                  <span style={styles.statusValue} data-testid="status-system-value">{health.isHealthy ? 'Healthy' : 'Degraded'}</span>
                </div>
              </div>
              {stats && (
                <>
                  <div style={styles.statusItem} className="nb-status-item" data-testid="status-documents">
                    <p style={styles.statusLabel}>Documents</p>
                    <p style={styles.statusValue} data-testid="status-documents-value">{(stats.documentCount ?? stats.totalDocuments ?? 0).toLocaleString()}</p>
                  </div>
                  <div style={styles.statusItem} className="nb-status-item" data-testid="status-chunks">
                    <p style={styles.statusLabel}>Chunks</p>
                    <p style={styles.statusValue} data-testid="status-chunks-value">{(stats.chunkCount ?? stats.totalChunks ?? 0).toLocaleString()}</p>
                  </div>
                  <div style={styles.statusItem} className="nb-status-item" data-testid="status-projects">
                    <p style={styles.statusLabel}>Projects</p>
                    <p style={styles.statusValue} data-testid="status-projects-value">{(stats.projectCount ?? 0).toLocaleString()}</p>
                  </div>
                </>
              )}
            </div>
          )}

          {activeTab === 'overview' && (
            <div style={styles.overviewGridContainer} className="nb-overview-grid" data-testid="panel-overview">
              {stats && (
                <div className="nb-card-shell nb-fade-up" style={{ animationDelay: '60ms' }}>
                  <IndexHealth stats={stats} />
                </div>
              )}
              {sources && (
                <div className="nb-card-shell nb-fade-up nb-overview-source-wide" style={{ animationDelay: '120ms' }}>
                  <SourceBreakdown sources={sources} />
                </div>
              )}
              <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
                <PerfTimeline metrics={performanceMetrics} />
              </div>
            </div>
          )}

          {activeTab === 'search' && (
            <div style={styles.gridContainer} data-testid="panel-search">
              <div className="nb-card-shell nb-fade-up">
                <SearchAnalytics />
              </div>
            </div>
          )}

          {activeTab === 'sources' && (
            <div style={styles.gridContainer} data-testid="panel-sources">
              {sources && (
                <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
                  <SourceManager sources={sources} onRefresh={refreshDashboard} />
                </div>
              )}
            </div>
          )}

          {activeTab === 'activity' && (
            <div style={styles.gridContainer} data-testid="panel-activity">
              {recentActivity && (
                <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
                  <ActivityFeed activities={recentActivity} />
                </div>
              )}
            </div>
          )}

          {activeTab === 'performance' && (
            <div style={styles.gridContainer} data-testid="panel-performance">
              <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
                <PerfTimeline metrics={performanceMetrics} />
              </div>
            </div>
          )}

          {activeTab === 'memory' && (
            <div style={styles.gridContainer} data-testid="panel-memory">
              <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
                <div style={styles.memoryScopeBar}>
                  <select
                    value={memoryScope}
                    onChange={(event) => setMemoryScope(event.target.value as MemoryScopeType)}
                    style={styles.memoryScopeInput}
                    data-testid="memory-scope-select"
                  >
                    <option value="global">Global</option>
                    <option value="project">Project</option>
                    <option value="session">Session</option>
                  </select>
                  <input
                    value={memoryScopeValue}
                    onChange={(event) => setMemoryScopeValue(event.target.value)}
                    disabled={memoryScope === 'global'}
                    placeholder={memoryScope === 'project' ? 'project-id (for example: NebulaRAG)' : memoryScope === 'session' ? 'session-id' : 'No value needed for global scope'}
                    style={styles.memoryScopeInput}
                    data-testid="memory-scope-value"
                  />
                  <button
                    onClick={applyMemoryScope}
                    disabled={loading}
                    style={styles.memoryScopeButton}
                    data-testid="memory-scope-apply"
                  >
                    Apply Scope
                  </button>
                </div>
                <div style={styles.memoryScopeStatusRow}>
                  <span style={styles.memoryScopeStatusLabel}>Applied Scope</span>
                  <span style={styles.memoryScopeBadge(appliedMemoryScope)} data-testid="memory-scope-badge">
                    {appliedMemoryScope === 'global' ? 'Global (all memories)' : `${appliedMemoryScope}: ${appliedMemoryScopeValue || 'n/a'}`}
                  </span>
                </div>
                <p style={styles.memoryScopeHint}>
                  Use scope controls to focus memory analytics while keeping global as the default overview.
                </p>
                <MemoryInsights stats={memoryStats} />
              </div>
            </div>
          )}
        </main>
      </div>
    </div>
  );
};

export default App;
