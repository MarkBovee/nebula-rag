import React from 'react';
import { nebulaTheme } from '@/styles/theme';
import type { IndexStats } from '@/types';

interface IndexHealthProps {
  stats: IndexStats;
}

const styles = {
  card: {
    background: nebulaTheme.colors.surface,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.lg,
    padding: nebulaTheme.spacing.lg,
    boxShadow: nebulaTheme.shadow.lg,
  } as React.CSSProperties,
  title: {
    fontSize: nebulaTheme.typography.fontSize.xl,
    fontWeight: nebulaTheme.typography.fontWeight.bold,
    marginBottom: nebulaTheme.spacing.lg,
    color: nebulaTheme.colors.neonCyan,
    textShadow: `0 0 10px ${nebulaTheme.colors.neonCyan}`,
  } as React.CSSProperties,
  metric: {
    marginBottom: nebulaTheme.spacing.lg,
    paddingBottom: nebulaTheme.spacing.lg,
    borderBottom: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
  } as React.CSSProperties,
  metricLabel: {
    fontSize: nebulaTheme.typography.fontSize.sm,
    color: nebulaTheme.colors.textSecondary,
    marginBottom: nebulaTheme.spacing.sm,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
  } as React.CSSProperties,
  metricValue: {
    fontSize: nebulaTheme.typography.fontSize['2xl'],
    fontWeight: nebulaTheme.typography.fontWeight.bold,
    color: nebulaTheme.colors.neonMagenta,
  } as React.CSSProperties,
  lastRow: {
    marginBottom: 0,
    paddingBottom: 0,
    borderBottom: 'none',
  } as React.CSSProperties,
};

/// <summary>
/// IndexHealth component displays vital index statistics.
/// Shows document count, chunk count, average chunk size, and vector dimensions.
/// </summary>
const IndexHealth: React.FC<IndexHealthProps> = ({ stats }) => {
  const formatBytes = (bytes: number | undefined) => {
    if (bytes === undefined) return 'N/A';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(2)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  };

  const lastUpdated = new Date(stats.lastUpdated).toLocaleString();

  return (
    <div style={styles.card}>
      <h2 style={styles.title}>📊 Index Health</h2>
      
      <div style={styles.metric}>
        <p style={styles.metricLabel}>Total Documents</p>
        <p style={styles.metricValue}>{stats.totalDocuments.toLocaleString()}</p>
      </div>

      <div style={styles.metric}>
        <p style={styles.metricLabel}>Total Chunks</p>
        <p style={styles.metricValue}>{stats.totalChunks.toLocaleString()}</p>
      </div>

      <div style={styles.metric}>
        <p style={styles.metricLabel}>Average Chunk Size</p>
        <p style={styles.metricValue}>
          {stats.averageChunkSize > 0 ? `${stats.averageChunkSize.toFixed(0)} chars` : 'N/A'}
        </p>
      </div>

      <div style={styles.metric}>
        <p style={styles.metricLabel}>Vector Dimensions</p>
        <p style={styles.metricValue}>{stats.vectorDimensions}</p>
      </div>

      <div style={styles.metric}>
        <p style={styles.metricLabel}>Index Size</p>
        <p style={styles.metricValue}>{formatBytes(stats.indexSizeBytes)}</p>
      </div>

      <div style={{ ...styles.metric, ...styles.lastRow }}>
        <p style={styles.metricLabel}>Last Updated</p>
        <p style={{ ...styles.metricValue, fontSize: nebulaTheme.typography.fontSize.sm }}>
          {lastUpdated}
        </p>
      </div>
    </div>
  );
};

export default IndexHealth;
