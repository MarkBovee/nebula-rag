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
    DateTime? NewestIndexedAt,
    /// <summary>Total on-disk size in bytes for the core index relations and indexes.</summary>
    long IndexSizeBytes,
    /// <summary>Number of distinct projects represented by indexed sources.</summary>
    int ProjectCount);

/// <summary>
/// Information about an indexed document source.
/// </summary>
public sealed record SourceInfo(
    /// <summary>The path or identifier for the indexed source document.</summary>
    string SourcePath,
    /// <summary>Optional explicit project identifier derived from source metadata.</summary>
    string? ProjectId,
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

/// <summary>
/// Per-project RAG index aggregates.
/// </summary>
public sealed record ProjectRagStats(
    /// <summary>Project identifier.</summary>
    string ProjectId,
    /// <summary>Number of indexed source documents for the project.</summary>
    int DocumentCount,
    /// <summary>Total number of chunks indexed for the project.</summary>
    int ChunkCount,
    /// <summary>Total token estimate across project chunks.</summary>
    long TotalTokens,
    /// <summary>Latest indexing timestamp for the project.</summary>
    DateTime? NewestIndexedAt);

/// <summary>
/// Per-project memory aggregates.
/// </summary>
public sealed record ProjectMemoryStats(
    /// <summary>Project identifier.</summary>
    string ProjectId,
    /// <summary>Total memory rows for the project.</summary>
    long MemoryCount,
    /// <summary>Latest memory timestamp for the project.</summary>
    DateTimeOffset? LastMemoryAtUtc);

/// <summary>
/// Per-project plan aggregates.
/// </summary>
public sealed record ProjectPlanStats(
    /// <summary>Project identifier.</summary>
    string ProjectId,
    /// <summary>Total plans for the project.</summary>
    int PlanCount,
    /// <summary>Total tasks across all plans for the project.</summary>
    int TaskCount,
    /// <summary>Number of active plans for the project.</summary>
    int ActivePlanCount,
    /// <summary>Latest plan update timestamp for the project.</summary>
    DateTimeOffset? LastUpdatedAtUtc);

/// <summary>
/// Hierarchical project dashboard node containing plans, RAG, and memory slices.
/// </summary>
public sealed record ProjectDashboardNode(
    /// <summary>Project identifier.</summary>
    string ProjectId,
    /// <summary>Plan slice for this project.</summary>
    ProjectPlanStats Plans,
    /// <summary>RAG slice for this project.</summary>
    ProjectRagStats Rag,
    /// <summary>Memory slice for this project.</summary>
    ProjectMemoryStats Memory);
