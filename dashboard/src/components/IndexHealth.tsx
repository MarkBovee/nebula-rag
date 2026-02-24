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
  const toCount = (value: number | undefined) => (typeof value === 'number' ? value : 0);

  const totalDocuments = toCount(stats.documentCount ?? stats.totalDocuments);
  const totalChunks = toCount(stats.chunkCount ?? stats.totalChunks);
  const totalTokens = toCount(stats.totalTokens);
  const averageChunkSize =
    stats.averageChunkSize ?? (totalChunks > 0 ? totalTokens / totalChunks : 0);
  const newestIndexedAt = stats.newestIndexedAt ?? stats.lastUpdated;
  const oldestIndexedAt = stats.oldestIndexedAt;

  const formatBytes = (bytes: number | undefined) => {
    if (bytes === undefined) return 'N/A';
    if (bytes < 1024) return `${bytes} B`;
    if (bytes < 1024 * 1024) return `${(bytes / 1024).toFixed(2)} KB`;
    return `${(bytes / (1024 * 1024)).toFixed(2)} MB`;
  };

  const lastUpdated = newestIndexedAt ? new Date(newestIndexedAt).toLocaleString() : 'N/A';
  const oldestIndexed = oldestIndexedAt ? new Date(oldestIndexedAt).toLocaleString() : 'N/A';

  return (
    <div style={styles.card}>
      <h2 style={styles.title}>Index Health</h2>
      
      <div style={styles.metric}>
        <p style={styles.metricLabel}>Total Documents</p>
        <p style={styles.metricValue}>{totalDocuments.toLocaleString()}</p>
      </div>

      <div style={styles.metric}>
        <p style={styles.metricLabel}>Total Chunks</p>
        <p style={styles.metricValue}>{totalChunks.toLocaleString()}</p>
      </div>

      <div style={styles.metric}>
        <p style={styles.metricLabel}>Average Chunk Size</p>
        <p style={styles.metricValue}>
          {averageChunkSize > 0 ? `${averageChunkSize.toFixed(0)} chars` : 'N/A'}
        </p>
      </div>

      <div style={styles.metric}>
        <p style={styles.metricLabel}>Total Tokens</p>
        <p style={styles.metricValue}>{totalTokens.toLocaleString()}</p>
      </div>

      <div style={styles.metric}>
        <p style={styles.metricLabel}>First Indexed</p>
        <p style={{ ...styles.metricValue, fontSize: nebulaTheme.typography.fontSize.sm }}>
          {oldestIndexed}
        </p>
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
