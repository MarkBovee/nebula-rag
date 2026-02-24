import type { SourceInfo } from '@/types';

export interface ProjectSourceSummary {
  projectName: string;
  sourceCount: number;
  chunkCount: number;
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
