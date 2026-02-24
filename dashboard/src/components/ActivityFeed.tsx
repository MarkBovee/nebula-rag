import React from 'react';
import { nebulaTheme } from '@/styles/theme';
import type { ActivityEvent } from '@/types';

interface ActivityFeedProps {
  activities: ActivityEvent[];
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
    color: nebulaTheme.colors.success,
    textShadow: `0 0 10px ${nebulaTheme.colors.success}`,
  } as React.CSSProperties,
  feedContainer: {
    maxHeight: '400px',
    overflowY: 'auto',
  } as React.CSSProperties,
  activityItem: {
    display: 'flex',
    gap: nebulaTheme.spacing.md,
    padding: nebulaTheme.spacing.md,
    borderLeft: `3px solid ${nebulaTheme.colors.neonCyan}`,
    marginBottom: nebulaTheme.spacing.md,
    background: 'rgba(0, 100, 150, 0.1)',
    borderRadius: nebulaTheme.borderRadius.md,
  } as React.CSSProperties,
  activityItemError: {
    borderLeftColor: nebulaTheme.colors.error,
    background: 'rgba(255, 51, 51, 0.05)',
  } as React.CSSProperties,
  timestamp: {
    fontSize: nebulaTheme.typography.fontSize.xs,
    color: nebulaTheme.colors.textMuted,
    whiteSpace: 'nowrap',
  } as React.CSSProperties,
  description: {
    fontSize: nebulaTheme.typography.fontSize.sm,
    color: nebulaTheme.colors.textPrimary,
  } as React.CSSProperties,
};

const getEventIcon = (eventType: string): string => {
  switch (eventType) {
    case 'index':
      return '📥';
    case 'query':
      return '🔍';
    case 'delete':
      return '🗑️';
    case 'error':
      return '⚠️';
    case 'mcp':
      return '🔌';
    case 'system':
      return '🛰️';
    default:
      return '•';
  }
};

const getEventColor = (eventType: string): string => {
  switch (eventType) {
    case 'index':
      return nebulaTheme.colors.success;
    case 'query':
      return nebulaTheme.colors.neonCyan;
    case 'delete':
      return nebulaTheme.colors.warning;
    case 'error':
      return nebulaTheme.colors.error;
    case 'mcp':
      return nebulaTheme.colors.neonPurple;
    case 'system':
      return nebulaTheme.colors.textSecondary;
    default:
      return nebulaTheme.colors.textSecondary;
  }
};

/// <summary>
/// ActivityFeed component displays real-time activity log of indexing, queries, and deletions.
/// Shows the most recent activities with timestamps and event descriptions.
/// </summary>
const ActivityFeed: React.FC<ActivityFeedProps> = ({ activities }) => {
  return (
    <div style={styles.card}>
      <h2 style={styles.title}>Realtime Activity</h2>
      
      <div style={styles.feedContainer}>
        {activities.length > 0 ? (
          activities.map((activity, idx) => (
            <div
              key={idx}
              style={{
                ...styles.activityItem,
                borderLeftColor: getEventColor(activity.eventType),
                background:
                  activity.eventType === 'error'
                    ? 'rgba(255, 51, 51, 0.05)'
                    : `rgba(${activity.eventType === 'index' ? '0, 255, 136' : '0, 217, 255'}, 0.05)`,
              }}
            >
              <span style={{ fontSize: nebulaTheme.typography.fontSize.lg }}>
                {getEventIcon(activity.eventType)}
              </span>
              <div style={{ flex: 1 }}>
                <p style={styles.description}>{activity.description}</p>
                <p style={styles.timestamp}>
                  {new Date(activity.timestampUtc ?? activity.timestamp).toLocaleTimeString()}
                </p>
              </div>
            </div>
          ))
        ) : (
          <p style={{ color: nebulaTheme.colors.textMuted, textAlign: 'center', padding: nebulaTheme.spacing.lg }}>
            No recent activity
          </p>
        )}
      </div>
    </div>
  );
};

export default ActivityFeed;
