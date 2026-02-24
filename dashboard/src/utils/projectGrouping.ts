import type { SourceInfo } from '@/types';

const secondaryProjectSegments = new Set(['workspace-notes', 'notes', 'docs']);
const genericRootSegments = new Set(['app', 'workspace', 'work', 'src', 'repo', 'project', 'projects', 'code']);

export interface ProjectSourceSummary {
  projectName: string;
  sourceCount: number;
  chunkCount: number;
}

/// <summary>
/// Extracts a project key from a source path for dashboard grouping/filtering.
/// </summary>
/// <param name="sourcePath">Indexed source path or URL identifier.</param>
/// <returns>Normalized project key label.</returns>
export const extractProjectName = (sourcePath: string): string => {
  if (!sourcePath) {
    return 'Unknown';
  }

  if (/^https?:\/\//i.test(sourcePath)) {
    try {
      return new URL(sourcePath).hostname;
    } catch {
      return 'Remote';
    }
  }

  const normalizedPath = sourcePath.replace(/\\/g, '/');
  const pathSegments = normalizedPath.split('/').filter(Boolean);

  if (pathSegments.length === 0) {
    return 'Unknown';
  }

  if (/^[A-Za-z]:$/.test(pathSegments[0])) {
    return pathSegments[1] || pathSegments[0];
  }

  if (pathSegments.length >= 2 && secondaryProjectSegments.has(pathSegments[0].toLowerCase())) {
    return pathSegments[1];
  }

  if (pathSegments.length >= 2 && genericRootSegments.has(pathSegments[0].toLowerCase())) {
    return pathSegments[1];
  }

  return pathSegments[0];
};

/// <summary>
/// Aggregates indexed sources into project-level counts and chunk totals.
/// </summary>
/// <param name="sources">Indexed source entries.</param>
/// <returns>Project summaries sorted by source count then project name.</returns>
export const summarizeSourcesByProject = (sources: SourceInfo[]): ProjectSourceSummary[] => {
  const aggregateMap = new Map<string, ProjectSourceSummary>();

  for (const source of sources) {
    const projectName = extractProjectName(source.sourcePath);
    const existingSummary = aggregateMap.get(projectName);

    if (!existingSummary) {
      aggregateMap.set(projectName, {
        projectName,
        sourceCount: 1,
        chunkCount: source.chunkCount,
      });
      continue;
    }

    existingSummary.sourceCount += 1;
    existingSummary.chunkCount += source.chunkCount;
  }

  return [...aggregateMap.values()].sort((left, right) => {
    if (right.sourceCount !== left.sourceCount) {
      return right.sourceCount - left.sourceCount;
    }

    return left.projectName.localeCompare(right.projectName);
  });
};
