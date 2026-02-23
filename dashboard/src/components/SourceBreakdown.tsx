import React from 'react';
import { PieChart, Pie, Cell, ResponsiveContainer, Tooltip } from 'recharts';
import { nebulaTheme, chartTheme } from '@/styles/theme';
import type { SourceInfo } from '@/types';

interface SourceBreakdownProps {
  sources: SourceInfo[];
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
    color: nebulaTheme.colors.neonMagenta,
    textShadow: `0 0 10px ${nebulaTheme.colors.neonMagenta}`,
  } as React.CSSProperties,
  chartContainer: {
    width: '100%',
    height: '300px',
  } as React.CSSProperties,
};

/// <summary>
/// SourceBreakdown component visualizes document distribution across sources using a pie chart.
/// Shows the proportion of chunks assigned to each source.
/// </summary>
const SourceBreakdown: React.FC<SourceBreakdownProps> = ({ sources }) => {
  const data = sources.map((source) => ({
    name: source.sourcePath.split('/').pop() || source.sourcePath,
    value: source.chunkCount,
    fullPath: source.sourcePath,
  }));

  const totalChunks = data.reduce((sum, item) => sum + item.value, 0);

  return (
    <div style={styles.card}>
      <h2 style={styles.title}>📦 Source Breakdown</h2>
      
      {data.length > 0 ? (
        <>
          <ResponsiveContainer width="100%" height={300}>
            <PieChart>
              <Pie
                data={data}
                cx="50%"
                cy="50%"
                innerRadius={60}
                outerRadius={100}
                paddingAngle={2}
                dataKey="value"
                label={({ value }) => `${((value / totalChunks) * 100).toFixed(0)}%`}
              >
                {data.map((_, index) => (
                  <Cell key={`cell-${index}`} fill={chartTheme.colors[index % chartTheme.colors.length]} />
                ))}
              </Pie>
              <Tooltip
                contentStyle={{
                  background: nebulaTheme.colors.surfaceLight,
                  border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
                  borderRadius: nebulaTheme.borderRadius.md,
                  color: nebulaTheme.colors.textPrimary,
                }}
                formatter={(value) => [`${value} chunks`, 'Chunks']}
              />
            </PieChart>
          </ResponsiveContainer>

          <div style={{ marginTop: nebulaTheme.spacing.lg }}>
            {data.map((item, idx) => (
              <div
                key={idx}
                style={{
                  display: 'flex',
                  justifyContent: 'space-between',
                  padding: nebulaTheme.spacing.sm,
                  borderBottom: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
                  fontSize: nebulaTheme.typography.fontSize.sm,
                }}
              >
                <span style={{ color: chartTheme.colors[idx % chartTheme.colors.length] }}>
                  ● {item.name}
                </span>
                <span style={{ color: nebulaTheme.colors.textSecondary }}>
                  {item.value} ({((item.value / totalChunks) * 100).toFixed(1)}%)
                </span>
              </div>
            ))}
          </div>
        </>
      ) : (
        <p style={{ color: nebulaTheme.colors.textMuted }}>No source data available</p>
      )}
    </div>
  );
};

export default SourceBreakdown;
