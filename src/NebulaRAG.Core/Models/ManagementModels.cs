namespace NebulaRAG.Core.Models;

/// <summary>
/// Statistics about the current RAG index.
/// </summary>
public sealed record IndexStats(
    int DocumentCount,
    int ChunkCount,
    long TotalTokens,
    DateTime? OldestIndexedAt,
    DateTime? NewestIndexedAt);

/// <summary>
/// Information about an indexed document source.
/// </summary>
public sealed record SourceInfo(
    string SourcePath,
    int ChunkCount,
    DateTime IndexedAt,
    string ContentHash);

/// <summary>
/// Result of a health check operation.
/// </summary>
public sealed record HealthCheckResult(
    bool IsHealthy,
    string Message);
