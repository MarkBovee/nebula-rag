import React, { useState, useEffect } from 'react';
import { nebulaTheme } from '@/styles/theme';
import { apiClient } from '@/api/client';
import type { DashboardState } from '@/types';
import IndexHealth from '@/components/IndexHealth';
import SearchAnalytics from '@/components/SearchAnalytics';
import SourceBreakdown from '@/components/SourceBreakdown';
import ActivityFeed from '@/components/ActivityFeed';
import SourceManager from '@/components/SourceManager';
import PerfTimeline from '@/components/PerfTimeline';

type DashboardTab = 'overview' | 'search' | 'sources' | 'activity' | 'performance';

const styles = {
  container: {
    background: `linear-gradient(135deg, ${nebulaTheme.colors.background} 0%, #1a0033 50%, #0f001a 100%)`,
    minHeight: '100vh',
    color: nebulaTheme.colors.textPrimary,
    fontFamily: nebulaTheme.typography.fontFamily,
    padding: nebulaTheme.spacing.lg,
  } as React.CSSProperties,
  header: {
    marginBottom: nebulaTheme.spacing['3xl'],
    borderBottom: `2px solid ${nebulaTheme.colors.surfaceBorder}`,
    paddingBottom: nebulaTheme.spacing.lg,
  } as React.CSSProperties,
  title: {
    fontSize: nebulaTheme.typography.fontSize['3xl'],
    fontWeight: nebulaTheme.typography.fontWeight.bold,
    marginBottom: nebulaTheme.spacing.md,
    textShadow: `0 0 20px ${nebulaTheme.colors.neonCyan}`,
  } as React.CSSProperties,
  subtitle: {
    fontSize: nebulaTheme.typography.fontSize.lg,
    color: nebulaTheme.colors.textSecondary,
  } as React.CSSProperties,
  statusBar: {
    display: 'flex',
    gap: nebulaTheme.spacing.lg,
    marginBottom: nebulaTheme.spacing.xl,
    flexWrap: 'wrap',
  } as React.CSSProperties,
  statusItem: {
    padding: `${nebulaTheme.spacing.md} ${nebulaTheme.spacing.lg}`,
    background: nebulaTheme.colors.surfaceLight,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.lg,
    display: 'flex',
    alignItems: 'center',
    gap: nebulaTheme.spacing.md,
  } as React.CSSProperties,
  healthDot: (isHealthy: boolean) => ({
    width: '12px',
    height: '12px',
    borderRadius: '50%',
    background: isHealthy ? nebulaTheme.colors.success : nebulaTheme.colors.error,
    boxShadow: isHealthy 
      ? `0 0 10px ${nebulaTheme.colors.success}` 
      : `0 0 10px ${nebulaTheme.colors.error}`,
  } as React.CSSProperties),
  gridContainer: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(400px, 1fr))',
    gap: nebulaTheme.spacing.xl,
    marginBottom: nebulaTheme.spacing['2xl'],
  } as React.CSSProperties,
  fullWidth: {
    gridColumn: '1 / -1',
  } as React.CSSProperties,
  tabBar: {
    display: 'flex',
    flexWrap: 'wrap',
    gap: nebulaTheme.spacing.sm,
    marginBottom: nebulaTheme.spacing.xl,
  } as React.CSSProperties,
  tabButton: (active: boolean) => ({
    padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
    borderRadius: nebulaTheme.borderRadius.md,
    border: `1px solid ${active ? nebulaTheme.colors.neonCyan : nebulaTheme.colors.surfaceBorder}`,
    background: active ? 'rgba(0, 217, 255, 0.15)' : nebulaTheme.colors.surfaceLight,
    color: active ? nebulaTheme.colors.neonCyan : nebulaTheme.colors.textSecondary,
    fontWeight: active ? nebulaTheme.typography.fontWeight.semibold : nebulaTheme.typography.fontWeight.medium,
    cursor: 'pointer',
    transition: nebulaTheme.transition.base,
    boxShadow: active ? nebulaTheme.shadow.glow.cyan : 'none',
  } as React.CSSProperties),
};

const tabs: Array<{ key: DashboardTab; label: string }> = [
  { key: 'overview', label: 'Overview' },
  { key: 'search', label: 'Search' },
  { key: 'sources', label: 'Sources' },
  { key: 'activity', label: 'Activity' },
  { key: 'performance', label: 'Performance' },
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
      const [health, stats, sources] = await Promise.all([
        apiClient.getHealth(),
        apiClient.getStats(),
        apiClient.listSources(50),
      ]);

      const activity = apiClient.getActivityLog();

      setDashboard({
        health,
        stats,
        sources,
        recentActivity: activity,
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
  /// Fetch dashboard data on component mount and set up auto-refresh every 10 seconds.
  /// </summary>
  useEffect(() => {
    refreshDashboard();
    const interval = setInterval(refreshDashboard, 10000);
    return () => clearInterval(interval);
  }, []);

  const { health, stats, sources, recentActivity, loading, error } = dashboard;

  return (
    <div style={styles.container}>
      {/* Header */}
      <div style={styles.header}>
        <h1 style={styles.title}>Nebula RAG</h1>
        <p style={styles.subtitle}>Enterprise-grade Document Retrieval & Indexing</p>
      </div>

      {/* Error Banner */}
      {error && (
        <div style={{
          ...styles.statusItem,
          background: 'rgba(255, 51, 51, 0.1)',
          borderColor: nebulaTheme.colors.error,
          marginBottom: nebulaTheme.spacing.lg,
        }}>
          <span style={{ color: nebulaTheme.colors.error }}>⚠ {error}</span>
        </div>
      )}

      {/* Health & Status Bar */}
      {health && (
        <div style={styles.statusBar}>
          <div style={styles.statusItem}>
            <div style={styles.healthDot(health.isHealthy)} />
            <span>System: {health.isHealthy ? 'Healthy' : 'Degraded'}</span>
          </div>
          {stats && (
            <>
              <div style={styles.statusItem}>
                <span style={{ color: nebulaTheme.colors.neonCyan }}>📄</span>
                <span>{(stats.documentCount ?? stats.totalDocuments ?? 0).toLocaleString()} Documents</span>
              </div>
              <div style={styles.statusItem}>
                <span style={{ color: nebulaTheme.colors.neonMagenta }}>📦</span>
                <span>{(stats.chunkCount ?? stats.totalChunks ?? 0).toLocaleString()} Chunks</span>
              </div>
            </>
          )}
          <button
            onClick={refreshDashboard}
            disabled={loading}
            style={{
              padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
              background: 'rgba(0, 217, 255, 0.1)',
              border: `1px solid ${nebulaTheme.colors.neonCyan}`,
              color: nebulaTheme.colors.neonCyan,
              cursor: 'pointer',
              borderRadius: nebulaTheme.borderRadius.md,
              fontWeight: nebulaTheme.typography.fontWeight.semibold,
              transition: nebulaTheme.transition.base,
            }}
            onMouseEnter={(e) => {
              (e.target as HTMLButtonElement).style.background = 'rgba(0, 217, 255, 0.2)';
              (e.target as HTMLButtonElement).style.boxShadow = nebulaTheme.shadow.glow.cyan;
            }}
            onMouseLeave={(e) => {
              (e.target as HTMLButtonElement).style.background = 'rgba(0, 217, 255, 0.1)';
              (e.target as HTMLButtonElement).style.boxShadow = 'none';
            }}
          >
            {loading ? '⟳ Refreshing...' : '⟳ Refresh'}
          </button>
        </div>
      )}

      {/* Topic Tabs */}
      <div style={styles.tabBar}>
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

      {/* Main Dashboard Grid */}
      {activeTab === 'overview' && (
        <div style={styles.gridContainer}>
          {stats && <IndexHealth stats={stats} />}
          {sources && <SourceBreakdown sources={sources} />}
          <div style={styles.fullWidth}>
            <PerfTimeline />
          </div>
        </div>
      )}

      {activeTab === 'search' && (
        <div style={styles.gridContainer}>
          <SearchAnalytics />
        </div>
      )}

      {activeTab === 'sources' && (
        <div style={styles.gridContainer}>
          {sources && (
            <div style={styles.fullWidth}>
              <SourceManager sources={sources} onRefresh={refreshDashboard} />
            </div>
          )}
        </div>
      )}

      {activeTab === 'activity' && (
        <div style={styles.gridContainer}>
          {recentActivity && (
            <div style={styles.fullWidth}>
              <ActivityFeed activities={recentActivity} />
            </div>
          )}
        </div>
      )}

      {activeTab === 'performance' && (
        <div style={styles.gridContainer}>
          <div style={styles.fullWidth}>
            <PerfTimeline />
          </div>
        </div>
      )}
    </div>
  );
};

export default App;
