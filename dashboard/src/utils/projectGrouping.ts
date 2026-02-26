import type { SourceInfo } from '@/types';

export interface ProjectSourceSummary {
  projectName: string;
  sourceCount: number;
  chunkCount: number;
}

interface SourceProjectLike {
  sourcePath: string;
  projectId?: string;
}

/// <summary>
/// Extracts a project key from a source path for dashboard grouping and filtering.
/// This intentionally avoids hardcoded segment-name heuristics.
/// </summary>
/// <param name="sourcePath">Indexed source path or URL identifier.</param>
/// <returns>Best-effort project key label.</returns>
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

  const projectBoundarySegments = new Set([
    '.github',
    'container',
    'dashboard',
    'docs',
    'nebula-rag',
    'scripts',
    'src',
    'tests',
  ]);

  const boundarySegmentIndex = pathSegments.findIndex((segment) => projectBoundarySegments.has(segment.toLowerCase()));
  if (boundarySegmentIndex > 0) {
    return pathSegments[boundarySegmentIndex - 1];
  }

  // Some source keys may include an outer workspace prefix like "Accentry.MiEP/NebulaRAG/...".
  // Prefer the next segment when the first token looks like a namespace/workspace marker.
  if (pathSegments.length > 1 && pathSegments[0].includes('.')) {
    return pathSegments[1];
  }

  // Most indexed source keys are stored as "projectName/...".
  if (!/^[A-Za-z]:$/.test(pathSegments[0])) {
    return pathSegments[0];
  }

  // Fallback for absolute Windows paths that were indexed without project prefix.
  const projectsSegmentIndex = pathSegments.findIndex((segment) => segment.toLowerCase() === 'projects');
  if (projectsSegmentIndex >= 0 && projectsSegmentIndex + 1 < pathSegments.length) {
    return pathSegments[projectsSegmentIndex + 1];
  }

  return pathSegments.length > 2 ? pathSegments[2] : pathSegments[1] ?? pathSegments[0];
};

/// <summary>
/// Resolves the project name for a source, preferring explicit project id from data.
/// </summary>
/// <param name="source">Source payload row.</param>
/// <returns>Resolved project display name.</returns>
export const getProjectNameForSource = (source: SourceProjectLike): string => {
  const projectId = source.projectId?.trim();
  if (projectId) {
    return projectId;
  }

  return extractProjectName(source.sourcePath);
};

/// <summary>
/// Aggregates indexed sources into project-level counts and chunk totals.
/// </summary>
/// <param name="sources">Indexed source entries.</param>
/// <returns>Project summaries sorted by source count then project name.</returns>
export const summarizeSourcesByProject = (sources: SourceInfo[]): ProjectSourceSummary[] => {
  const aggregateMap = new Map<string, ProjectSourceSummary>();

  for (const source of sources) {
    const projectName = getProjectNameForSource(source);
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
