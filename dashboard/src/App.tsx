import React, { useState, useEffect } from 'react';
import { nebulaTheme, getBackgroundGradient } from '@/styles/theme';
import { apiClient } from '@/api/client';
import type { DashboardState } from '@/types';
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

  /// <summary>
  /// Refreshes all dashboard data by fetching from the API.
  /// Handles errors gracefully and updates the dashboard state.
  /// </summary>
  const refreshDashboard = async () => {
    setDashboard(prev => ({ ...prev, loading: true, error: undefined }));
    try {
      const snapshot = await apiClient.getDashboard(50);

      setDashboard({
        health: snapshot.health,
        stats: snapshot.stats,
        sources: snapshot.sources,
        memoryStats: snapshot.memoryStats,
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
    <div style={styles.container} className="nb-app">
      {/* Header */}
      <div style={styles.shell} className="nb-shell">
        <aside style={styles.nav} className="nb-nav nb-fade-up">
          <h1 style={styles.navTitle}>Nebula RAG</h1>
          <p style={styles.navSubtitle}>Control Center</p>

          <div style={styles.tabBar} className="nb-tabbar">
            {tabs.map((tab) => (
              <button
                key={tab.key}
                onClick={() => setActiveTab(tab.key)}
                style={styles.tabButton(activeTab === tab.key)}
                aria-pressed={activeTab === tab.key}
              >
                {tab.label}
              </button>
            ))}
          </div>

          <button onClick={refreshDashboard} disabled={loading} style={styles.refreshButton}>
            {loading ? 'Refreshing...' : 'Refresh Data'}
          </button>
        </aside>

        <main style={styles.content} className="nb-content">
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
              <div style={styles.statusItem}>
                <p style={styles.statusLabel}>System Status</p>
                <div style={{ display: 'flex', alignItems: 'center', gap: nebulaTheme.spacing.sm }}>
                  <div style={styles.healthDot(health.isHealthy)} />
                  <span style={styles.statusValue}>{health.isHealthy ? 'Healthy' : 'Degraded'}</span>
                </div>
              </div>
              {stats && (
                <>
                  <div style={styles.statusItem}>
                    <p style={styles.statusLabel}>Documents</p>
                    <p style={styles.statusValue}>{(stats.documentCount ?? stats.totalDocuments ?? 0).toLocaleString()}</p>
                  </div>
                  <div style={styles.statusItem}>
                    <p style={styles.statusLabel}>Chunks</p>
                    <p style={styles.statusValue}>{(stats.chunkCount ?? stats.totalChunks ?? 0).toLocaleString()}</p>
                  </div>
                  <div style={styles.statusItem}>
                    <p style={styles.statusLabel}>Projects</p>
                    <p style={styles.statusValue}>{(stats.projectCount ?? 0).toLocaleString()}</p>
                  </div>
                </>
              )}
            </div>
          )}

          {activeTab === 'overview' && (
            <div style={styles.overviewGridContainer} className="nb-overview-grid">
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
            <div style={styles.gridContainer}>
              <div className="nb-card-shell nb-fade-up">
                <SearchAnalytics />
              </div>
            </div>
          )}

          {activeTab === 'sources' && (
            <div style={styles.gridContainer}>
              {sources && (
                <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
                  <SourceManager sources={sources} onRefresh={refreshDashboard} />
                </div>
              )}
            </div>
          )}

          {activeTab === 'activity' && (
            <div style={styles.gridContainer}>
              {recentActivity && (
                <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
                  <ActivityFeed activities={recentActivity} />
                </div>
              )}
            </div>
          )}

          {activeTab === 'performance' && (
            <div style={styles.gridContainer}>
              <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
                <PerfTimeline metrics={performanceMetrics} />
              </div>
            </div>
          )}

          {activeTab === 'memory' && (
            <div style={styles.gridContainer}>
              <div style={styles.fullWidth} className="nb-card-shell nb-fade-up">
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
