import React, { useMemo, useState } from 'react';
import { nebulaTheme } from '@/styles/theme';
import { apiClient } from '@/api/client';
import type { SourceInfo } from '@/types';
import { getProjectNameForSource } from '@/utils/projectGrouping';

interface SourceManagerProps {
  sources: SourceInfo[];
  onRefresh: () => void;
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
    color: nebulaTheme.colors.neonPink,
    textShadow: `0 0 10px ${nebulaTheme.colors.neonPink}`,
  } as React.CSSProperties,
  table: {
    width: '100%',
    borderCollapse: 'collapse' as const,
  } as React.CSSProperties,
  thead: {
    background: 'rgba(100, 50, 200, 0.1)',
    borderBottom: `2px solid ${nebulaTheme.colors.surfaceBorder}`,
  } as React.CSSProperties,
  th: {
    padding: nebulaTheme.spacing.md,
    textAlign: 'left' as const,
    fontWeight: nebulaTheme.typography.fontWeight.semibold,
    color: nebulaTheme.colors.neonCyan,
    fontSize: nebulaTheme.typography.fontSize.sm,
  } as React.CSSProperties,
  td: {
    padding: nebulaTheme.spacing.md,
    borderBottom: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    fontSize: nebulaTheme.typography.fontSize.sm,
  } as React.CSSProperties,
  actionButton: {
    padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
    marginRight: nebulaTheme.spacing.sm,
    border: 'none',
    borderRadius: nebulaTheme.borderRadius.md,
    cursor: 'pointer',
    fontSize: nebulaTheme.typography.fontSize.xs,
    fontWeight: nebulaTheme.typography.fontWeight.semibold,
    transition: nebulaTheme.transition.fast,
  } as React.CSSProperties,
  deleteButton: {
    background: 'rgba(255, 51, 51, 0.2)',
    color: nebulaTheme.colors.error,
    border: `1px solid ${nebulaTheme.colors.error}`,
  } as React.CSSProperties,
  reindexButton: {
    background: 'rgba(255, 170, 0, 0.2)',
    color: nebulaTheme.colors.warning,
    border: `1px solid ${nebulaTheme.colors.warning}`,
  } as React.CSSProperties,
  filterRow: {
    display: 'flex',
    alignItems: 'center',
    justifyContent: 'space-between',
    gap: nebulaTheme.spacing.md,
    marginBottom: nebulaTheme.spacing.md,
    flexWrap: 'wrap',
  } as React.CSSProperties,
  filterLabel: {
    color: nebulaTheme.colors.textSecondary,
    fontSize: nebulaTheme.typography.fontSize.sm,
    textTransform: 'uppercase',
    letterSpacing: '0.05em',
  } as React.CSSProperties,
  filterSelect: {
    background: nebulaTheme.colors.surfaceLight,
    color: nebulaTheme.colors.textPrimary,
    border: `1px solid ${nebulaTheme.colors.surfaceBorder}`,
    borderRadius: nebulaTheme.borderRadius.md,
    padding: `${nebulaTheme.spacing.sm} ${nebulaTheme.spacing.md}`,
    minWidth: '220px',
  } as React.CSSProperties,
};

/// <summary>
/// SourceManager component provides a table view of all indexed sources.
/// Users can delete sources or trigger reindexing from this interface.
/// </summary>
const SourceManager: React.FC<SourceManagerProps> = ({ sources, onRefresh }) => {
  const [deleting, setDeleting] = useState<string | null>(null);
  const [reindexing, setReindexing] = useState<string | null>(null);
  const [selectedProject, setSelectedProject] = useState<string>('ALL_PROJECTS');

  const projectNames = useMemo(() => {
    const distinctProjectNames = [...new Set(sources.map((source) => getProjectNameForSource(source)))];
    return distinctProjectNames.sort((left, right) => left.localeCompare(right));
  }, [sources]);

  const sourceCountByProject = useMemo(() => {
    const projectCountMap = new Map<string, number>();

    for (const source of sources) {
      const projectName = getProjectNameForSource(source);
      projectCountMap.set(projectName, (projectCountMap.get(projectName) ?? 0) + 1);
    }

    return projectCountMap;
  }, [sources]);

  const filteredSources = useMemo(() => {
    if (selectedProject === 'ALL_PROJECTS') {
      return sources;
    }

    return sources.filter((source) => getProjectNameForSource(source) === selectedProject);
  }, [selectedProject, sources]);

  const handleDelete = async (sourcePath: string) => {
    if (!confirm(`Delete source "${sourcePath}"? This cannot be undone.`)) return;

    setDeleting(sourcePath);
    try {
      await apiClient.deleteSource(sourcePath);
      setTimeout(onRefresh, 1000);
    } catch (error) {
      console.error('Delete error:', error);
      alert('Failed to delete source');
    }
    setDeleting(null);
  };

  const handleReindex = async (sourcePath: string) => {
    setReindexing(sourcePath);
    try {
      await apiClient.indexSource(sourcePath);
      setTimeout(onRefresh, 2000);
    } catch (error) {
      console.error('Reindex error:', error);
      alert('Failed to reindex source');
    }
    setReindexing(null);
  };

  return (
    <div style={styles.card} data-testid="sources-card">
      <h2 style={styles.title}>Source Management</h2>

      <div style={styles.filterRow}>
        <span style={styles.filterLabel}>Project Filter</span>
        <select
          style={styles.filterSelect}
          value={selectedProject}
          onChange={(event) => setSelectedProject(event.target.value)}
          data-testid="sources-project-filter"
        >
          <option value="ALL_PROJECTS">All Projects ({sources.length})</option>
          {projectNames.map((projectName) => {
            const sourceCountForProject = sourceCountByProject.get(projectName) ?? 0;
            return (
              <option key={projectName} value={projectName}>
                {projectName} ({sourceCountForProject})
              </option>
            );
          })}
        </select>
      </div>
      
      {filteredSources.length > 0 ? (
        <div style={{ overflowX: 'auto' }} className="nb-source-table-wrap">
          <table style={styles.table} data-testid="sources-table">
            <thead style={styles.thead}>
              <tr>
                <th style={styles.th}>Source Path</th>
                <th style={styles.th}>Documents</th>
                <th style={styles.th}>Chunks</th>
                <th style={styles.th}>Last Indexed</th>
                <th style={styles.th}>Actions</th>
              </tr>
            </thead>
            <tbody>
              {filteredSources.map((source, idx) => (
                <tr key={idx} data-testid="source-row">
                  <td style={styles.td}>
                    <span style={{ color: nebulaTheme.colors.neonCyan }} className="nb-source-path" data-testid="source-path">
                      {source.sourcePath}
                    </span>
                  </td>
                  <td style={styles.td}>{source.documentCount ?? '-'}</td>
                  <td style={styles.td}>{source.chunkCount}</td>
                  <td style={styles.td}>
                    {new Date(source.indexedAt ?? source.lastIndexedAt ?? '').toLocaleDateString()}
                  </td>
                  <td style={styles.td}>
                    <button
                      onClick={() => handleReindex(source.sourcePath)}
                      disabled={reindexing === source.sourcePath}
                      data-testid="source-reindex-button"
                      style={{
                        ...styles.actionButton,
                        ...styles.reindexButton,
                      }}
                    >
                      {reindexing === source.sourcePath ? '⟳' : '🔄'} Reindex
                    </button>
                    <button
                      onClick={() => handleDelete(source.sourcePath)}
                      disabled={deleting === source.sourcePath}
                      data-testid="source-delete-button"
                      style={{
                        ...styles.actionButton,
                        ...styles.deleteButton,
                      }}
                    >
                      {deleting === source.sourcePath ? '⟳' : '🗑️'} Delete
                    </button>
                  </td>
                </tr>
              ))}
            </tbody>
          </table>
        </div>
      ) : (
        <p style={{ color: nebulaTheme.colors.textMuted, textAlign: 'center', padding: nebulaTheme.spacing.lg }} data-testid="sources-empty-state">
          No sources found for this project filter
        </p>
      )}
    </div>
  );
};

export default SourceManager;
