namespace NebulaRAG.Core.Models;

/// <summary>
/// Statistics about the current RAG index.
/// </summary>
public sealed record IndexStats(
    /// <summary>Number of distinct indexed source documents.</summary>
    int DocumentCount,
    /// <summary>Total number of indexed chunks across all documents.</summary>
    int ChunkCount,
    /// <summary>Combined token count estimate for all indexed chunks.</summary>
    long TotalTokens,
    /// <summary>UTC timestamp of the earliest document indexed, if any.</summary>
    DateTime? OldestIndexedAt,
    /// <summary>UTC timestamp of the most recent document indexed, if any.</summary>
    DateTime? NewestIndexedAt);

/// <summary>
/// Information about an indexed document source.
/// </summary>
public sealed record SourceInfo(
    /// <summary>The path or identifier for the indexed source document.</summary>
    string SourcePath,
    /// <summary>Number of chunks currently stored for this source.</summary>
    int ChunkCount,
    /// <summary>UTC timestamp when this source was last indexed.</summary>
    DateTime IndexedAt,
    /// <summary>SHA256 hash of the source content for change detection.</summary>
    string ContentHash);

/// <summary>
/// Result of a database health check operation.
/// </summary>
public sealed record HealthCheckResult(
    /// <summary>True if the health check succeeded; false otherwise.</summary>
    bool IsHealthy,
    /// <summary>Status message or error details.</summary>
    string Message);
