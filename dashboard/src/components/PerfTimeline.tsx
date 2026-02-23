import React, { useMemo } from 'react';
import { LineChart, Line, XAxis, YAxis, CartesianGrid, Tooltip, Legend, ResponsiveContainer } from 'recharts';
import { nebulaTheme, chartTheme } from '@/styles/theme';

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
    color: nebulaTheme.colors.neonPurple,
    textShadow: `0 0 10px ${nebulaTheme.colors.neonPurple}`,
  } as React.CSSProperties,
  chartContainer: {
    width: '100%',
    height: '350px',
  } as React.CSSProperties,
};

/// <summary>
/// PerfTimeline component displays performance metrics over the last 24 hours.
/// Shows query latency and indexing rates as trends over time.
/// </summary>
const PerfTimeline: React.FC = () => {
  // Generate mock performance data for demonstration
  const data = useMemo(() => {
    const now = new Date();
    const data = [];
    for (let i = 23; i >= 0; i--) {
      const time = new Date(now);
      time.setHours(time.getHours() - i);
      data.push({
        time: time.toLocaleTimeString([], { hour: '2-digit', minute: '2-digit' }),
        queryLatency: 45 + Math.random() * 30, // 45-75ms
        indexingRate: 10 + Math.random() * 40, // 10-50 docs/sec
        cpuUsage: 20 + Math.random() * 30, // 20-50%
      });
    }
    return data;
  }, []);

  return (
    <div style={styles.card}>
      <h2 style={styles.title}>📈 Performance Timeline (24h)</h2>
      
      <ResponsiveContainer width="100%" height={350}>
        <LineChart data={data} margin={{ top: 5, right: 30, left: 0, bottom: 5 }}>
          <CartesianGrid
            strokeDasharray="3 3"
            stroke={chartTheme.grid.stroke}
          />
          <XAxis
            dataKey="time"
            stroke={chartTheme.axis.stroke}
            tick={{ fill: chartTheme.axis.tick.fill, fontSize: 12 }}
          />
          <YAxis
            stroke={chartTheme.axis.stroke}
            tick={{ fill: chartTheme.axis.tick.fill, fontSize: 12 }}
            yAxisId="left"
            label={{ value: 'Query Latency (ms)', angle: -90, position: 'insideLeft' }}
          />
          <YAxis
            stroke={chartTheme.axis.stroke}
            tick={{ fill: chartTheme.axis.tick.fill, fontSize: 12 }}
            yAxisId="right"
            orientation="right"
            label={{ value: 'Docs/sec', angle: 90, position: 'insideRight' }}
          />
          <Tooltip
            contentStyle={{
              background: nebulaTheme.colors.surfaceLight,
              border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
              borderRadius: nebulaTheme.borderRadius.md,
              color: nebulaTheme.colors.textPrimary,
            }}
          />
          <Legend
            wrapperStyle={{
              color: nebulaTheme.colors.textSecondary,
              paddingTop: nebulaTheme.spacing.lg,
            }}
          />
          <Line
            yAxisId="left"
            type="monotone"
            dataKey="queryLatency"
            stroke={nebulaTheme.colors.neonCyan}
            strokeWidth={2}
            dot={false}
            name="Query Latency (ms)"
          />
          <Line
            yAxisId="right"
            type="monotone"
            dataKey="indexingRate"
            stroke={nebulaTheme.colors.neonMagenta}
            strokeWidth={2}
            dot={false}
            name="Indexing Rate (docs/sec)"
          />
          <Line
            yAxisId="right"
            type="monotone"
            dataKey="cpuUsage"
            stroke={nebulaTheme.colors.neonPink}
            strokeWidth={2}
            dot={false}
            name="CPU Usage (%)"
          />
        </LineChart>
      </ResponsiveContainer>
    </div>
  );
};

export default PerfTimeline;
