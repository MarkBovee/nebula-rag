import React, { useMemo } from 'react';
import { Bar, BarChart, CartesianGrid, Cell, Line, LineChart, Pie, PieChart, ResponsiveContainer, Tooltip, XAxis, YAxis } from 'recharts';
import { nebulaTheme, chartTheme } from '@/styles/theme';
import type { MemoryDashboardStats } from '@/types';

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
    color: nebulaTheme.colors.neonBlue,
    textShadow: `0 0 10px ${nebulaTheme.colors.neonBlue}`,
  } as React.CSSProperties,
  summaryGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(180px, 1fr))',
    gap: nebulaTheme.spacing.md,
    marginBottom: nebulaTheme.spacing.lg,
  } as React.CSSProperties,
  summaryTile: {
    background: nebulaTheme.colors.surfaceLight,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.md,
    padding: nebulaTheme.spacing.md,
  } as React.CSSProperties,
  summaryLabel: {
    fontSize: nebulaTheme.typography.fontSize.xs,
    color: nebulaTheme.colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: '0.08em',
    marginBottom: nebulaTheme.spacing.xs,
  } as React.CSSProperties,
  summaryValue: {
    fontSize: nebulaTheme.typography.fontSize['2xl'],
    fontWeight: nebulaTheme.typography.fontWeight.bold,
    fontFamily: "'Rajdhani', sans-serif",
  } as React.CSSProperties,
  chartsGrid: {
    display: 'grid',
    gridTemplateColumns: 'repeat(auto-fit, minmax(320px, 1fr))',
    gap: nebulaTheme.spacing.lg,
    marginBottom: nebulaTheme.spacing.lg,
  } as React.CSSProperties,
  chartPanel: {
    background: nebulaTheme.colors.surfaceLight,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.md,
    padding: nebulaTheme.spacing.md,
    minHeight: '320px',
  } as React.CSSProperties,
  panelTitle: {
    fontSize: nebulaTheme.typography.fontSize.sm,
    color: nebulaTheme.colors.textSecondary,
    marginBottom: nebulaTheme.spacing.md,
    textTransform: 'uppercase',
    letterSpacing: '0.08em',
  } as React.CSSProperties,
  table: {
    width: '100%',
    borderCollapse: 'collapse' as const,
    fontSize: nebulaTheme.typography.fontSize.sm,
  },
  tableCell: {
    padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
    borderBottom: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    color: nebulaTheme.colors.textSecondary,
  } as React.CSSProperties,
  tableHeader: {
    color: nebulaTheme.colors.textMuted,
    textTransform: 'uppercase',
    letterSpacing: '0.08em',
    fontSize: nebulaTheme.typography.fontSize.xs,
  } as React.CSSProperties,
  emptyState: {
    color: nebulaTheme.colors.textMuted,
    fontSize: nebulaTheme.typography.fontSize.sm,
  } as React.CSSProperties,
};

interface MemoryInsightsProps {
  stats?: MemoryDashboardStats;
}

/// <summary>
/// Renders full memory analytics including summary KPIs, type/tag charts, daily trend, and recent sessions.
/// </summary>
/// <param name="stats">Memory analytics payload from the backend dashboard snapshot.</param>
const MemoryInsights: React.FC<MemoryInsightsProps> = ({ stats }) => {
  const typeData = useMemo(() => stats?.typeCounts ?? [], [stats]);
  const tagData = useMemo(() => {
    const normalizeTagLabel = (tag: string): string => {
      const normalizedTag = tag.includes(':') ? tag.split(':')[1] : tag;
      return normalizedTag.length > 22 ? `${normalizedTag.slice(0, 22)}...` : normalizedTag;
    };

    return (stats?.topTags ?? []).map((entry) => ({
      ...entry,
      displayTag: normalizeTagLabel(entry.tag),
    }));
  }, [stats]);
  const dailyData = useMemo(() => {
    return (stats?.dailyCounts ?? []).map((entry) => ({
      date: new Date(entry.dateUtc).toLocaleDateString([], { month: 'short', day: '2-digit' }),
      count: entry.count,
    }));
  }, [stats]);

  if (!stats) {
    return (
      <div style={styles.card}>
        <h2 style={styles.title}>Memory Insights</h2>
        <p style={styles.emptyState}>Memory analytics are loading.</p>
      </div>
    );
  }

  return (
    <div style={styles.card}>
      <h2 style={styles.title}>Memory Insights</h2>

      <div style={styles.summaryGrid}>
        <div style={styles.summaryTile}>
          <p style={styles.summaryLabel}>Total Memories</p>
          <p style={styles.summaryValue}>{stats.totalMemories.toLocaleString()}</p>
        </div>
        <div style={styles.summaryTile}>
          <p style={styles.summaryLabel}>Last 24 Hours</p>
          <p style={styles.summaryValue}>{stats.recent24HoursCount.toLocaleString()}</p>
        </div>
        <div style={styles.summaryTile}>
          <p style={styles.summaryLabel}>Distinct Sessions</p>
          <p style={styles.summaryValue}>{stats.distinctSessionCount.toLocaleString()}</p>
        </div>
        <div style={styles.summaryTile}>
          <p style={styles.summaryLabel}>Avg Tags Per Memory</p>
          <p style={styles.summaryValue}>{stats.averageTagsPerMemory.toFixed(2)}</p>
        </div>
      </div>

      <div style={styles.chartsGrid}>
        <div style={styles.chartPanel}>
          <p style={styles.panelTitle}>Type Distribution</p>
          <ResponsiveContainer width="100%" height={260}>
            <PieChart>
              <Pie data={typeData} dataKey="count" nameKey="type" outerRadius={88} innerRadius={42}>
                {typeData.map((entry, index) => (
                  <Cell key={entry.type} fill={chartTheme.colors[index % chartTheme.colors.length]} />
                ))}
              </Pie>
              <Tooltip
                contentStyle={{
                  background: nebulaTheme.colors.surfaceLight,
                  border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
                  borderRadius: nebulaTheme.borderRadius.md,
                }}
              />
            </PieChart>
          </ResponsiveContainer>
        </div>

        <div style={styles.chartPanel}>
          <p style={styles.panelTitle}>Top Tags</p>
          <ResponsiveContainer width="100%" height={260}>
            <BarChart data={tagData} layout="vertical" margin={{ top: 8, right: 12, left: 8, bottom: 8 }}>
              <CartesianGrid strokeDasharray="3 3" stroke={chartTheme.grid.stroke} />
              <XAxis type="number" tick={{ fill: chartTheme.axis.tick.fill, fontSize: 11 }} allowDecimals={false} />
              <YAxis type="category" dataKey="displayTag" width={150} tick={{ fill: chartTheme.axis.tick.fill, fontSize: 11 }} />
              <Tooltip
                formatter={(value: number, _: string, payload: any) => [value, payload?.payload?.tag ?? 'tag']}
                contentStyle={{
                  background: nebulaTheme.colors.surfaceLight,
                  border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
                  borderRadius: nebulaTheme.borderRadius.md,
                }}
              />
              <Bar dataKey="count" fill={nebulaTheme.colors.accentSecondary} radius={[0, 6, 6, 0]} />
            </BarChart>
          </ResponsiveContainer>
        </div>

        <div style={{ ...styles.chartPanel, gridColumn: '1 / -1' }}>
          <p style={styles.panelTitle}>Memory Growth (Daily)</p>
          <ResponsiveContainer width="100%" height={260}>
            <LineChart data={dailyData} margin={{ top: 8, right: 12, left: 0, bottom: 12 }}>
              <CartesianGrid strokeDasharray="3 3" stroke={chartTheme.grid.stroke} />
              <XAxis dataKey="date" tick={{ fill: chartTheme.axis.tick.fill, fontSize: 11 }} />
              <YAxis tick={{ fill: chartTheme.axis.tick.fill, fontSize: 11 }} allowDecimals={false} />
              <Tooltip
                contentStyle={{
                  background: nebulaTheme.colors.surfaceLight,
                  border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
                  borderRadius: nebulaTheme.borderRadius.md,
                }}
              />
              <Line type="monotone" dataKey="count" stroke={nebulaTheme.colors.accentPrimary} strokeWidth={2.5} dot={{ r: 3 }} />
            </LineChart>
          </ResponsiveContainer>
        </div>
      </div>

      <div style={styles.chartPanel}>
        <p style={styles.panelTitle}>Recent Sessions</p>
        {stats.recentSessions.length === 0 ? (
          <p style={styles.emptyState}>No memory sessions found.</p>
        ) : (
          <table style={styles.table}>
            <thead>
              <tr>
                <th style={{ ...styles.tableCell, ...styles.tableHeader, textAlign: 'left' }}>Session</th>
                <th style={{ ...styles.tableCell, ...styles.tableHeader, textAlign: 'right' }}>Memories</th>
                <th style={{ ...styles.tableCell, ...styles.tableHeader, textAlign: 'right' }}>Last Write (UTC)</th>
              </tr>
            </thead>
            <tbody>
              {stats.recentSessions.map((session) => (
                <tr key={session.sessionId}>
                  <td style={{ ...styles.tableCell, color: nebulaTheme.colors.textPrimary }}>{session.sessionId}</td>
                  <td style={{ ...styles.tableCell, textAlign: 'right' }}>{session.memoryCount.toLocaleString()}</td>
                  <td style={{ ...styles.tableCell, textAlign: 'right' }}>
                    {new Date(session.lastMemoryAtUtc).toLocaleString([], { hour12: false })}
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        )}
      </div>
    </div>
  );
};

export default MemoryInsights;
