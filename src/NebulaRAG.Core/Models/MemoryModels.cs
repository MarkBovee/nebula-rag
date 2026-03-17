namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents a stored memory record with metadata.
/// </summary>
/// <param name="Id">Database identifier of the memory entry.</param>
/// <param name="SessionId">Logical session identifier associated with the memory.</param>
/// <param name="ProjectId">Optional project identifier associated with the memory.</param>
/// <param name="Type">Memory type: episodic, semantic, or procedural.</param>
/// <param name="Content">Natural language memory content.</param>
/// <param name="Tags">Tags associated with the memory for filtering.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the memory was created.</param>
public sealed record MemoryRecord(long Id, string SessionId, string? ProjectId, string Type, string Content, IReadOnlyList<string> Tags, DateTimeOffset CreatedAtUtc);

/// <summary>
/// Represents a memory recall result including semantic score.
/// </summary>
/// <param name="Id">Database identifier of the recalled memory entry.</param>
/// <param name="SessionId">Logical session identifier associated with the memory.</param>
/// <param name="ProjectId">Optional project identifier associated with the memory.</param>
/// <param name="Type">Memory type: episodic, semantic, or procedural.</param>
/// <param name="Content">Natural language memory content.</param>
/// <param name="Tags">Tags associated with the memory for filtering.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the memory was created.</param>
/// <param name="Score">Final blended ranking score.</param>
/// <param name="SemanticScore">Cosine similarity score against the recall query.</param>
/// <param name="LexicalScore">BM25/lexical search score.</param>
/// <param name="RecencyBoost">Recency-based boost factor.</param>
/// <param name="TagBoost">Tag-based boost factor.</param>
/// <param name="UsedFallback">Whether fallback ranking was used.</param>
public sealed record MemorySearchResult(
    long Id,
    string SessionId,
    string? ProjectId,
    string Type,
    string Content,
    IReadOnlyList<string> Tags,
    DateTimeOffset CreatedAtUtc,
    double Score,
    double? SemanticScore = null,
    double? LexicalScore = null,
    double? RecencyBoost = null,
    double? TagBoost = null,
    bool UsedFallback = false);

/// <summary>
/// Aggregated memory analytics payload used by dashboard and API consumers.
/// </summary>
/// <param name="TotalMemories">Total number of stored memories.</param>
/// <param name="Recent24HoursCount">Number of memories created in the last 24 hours.</param>
/// <param name="DistinctSessionCount">Number of distinct session identifiers represented in memory records.</param>
/// <param name="DistinctProjectCount">Number of distinct project identifiers represented in memory records.</param>
/// <param name="AverageTagsPerMemory">Average number of tags attached per memory entry.</param>
/// <param name="FirstMemoryAtUtc">Oldest memory timestamp, or <c>null</c> when no memories exist.</param>
/// <param name="LastMemoryAtUtc">Newest memory timestamp, or <c>null</c> when no memories exist.</param>
/// <param name="TypeCounts">Breakdown by memory type.</param>
/// <param name="TopTags">Most frequently used memory tags.</param>
/// <param name="DailyCounts">Daily memory creation totals over a configured window.</param>
/// <param name="RecentSessions">Most recently active sessions with memory counts.</param>
public sealed record MemoryDashboardStats(
	long TotalMemories,
	long Recent24HoursCount,
	int DistinctSessionCount,
	int DistinctProjectCount,
	double AverageTagsPerMemory,
	DateTimeOffset? FirstMemoryAtUtc,
	DateTimeOffset? LastMemoryAtUtc,
	IReadOnlyList<MemoryTypeCount> TypeCounts,
	IReadOnlyList<MemoryTagCount> TopTags,
	IReadOnlyList<MemoryDailyCount> DailyCounts,
	IReadOnlyList<MemorySessionSummary> RecentSessions);

/// <summary>
/// Count summary for one memory type.
/// </summary>
/// <param name="Type">Memory type label.</param>
/// <param name="Count">Count of rows for this type.</param>
public sealed record MemoryTypeCount(string Type, long Count);

/// <summary>
/// Count summary for one memory tag.
/// </summary>
/// <param name="Tag">Tag value.</param>
/// <param name="Count">Count of memories containing this tag.</param>
public sealed record MemoryTagCount(string Tag, long Count);

/// <summary>
/// Count summary for one UTC day.
/// </summary>
/// <param name="DateUtc">UTC date bucket for this count.</param>
/// <param name="Count">Number of memories created on the date.</param>
public sealed record MemoryDailyCount(DateOnly DateUtc, long Count);

/// <summary>
/// Session-level memory summary.
/// </summary>
/// <param name="SessionId">Session identifier.</param>
/// <param name="MemoryCount">Number of memories in the session.</param>
/// <param name="LastMemoryAtUtc">Most recent memory timestamp for the session.</param>
public sealed record MemorySessionSummary(string SessionId, long MemoryCount, DateTimeOffset LastMemoryAtUtc);

/// <summary>
/// Recall mode preset for different ranking strategies.
/// </summary>
public sealed enum MemoryRecallMode
{
    /// <summary>Precise mode with strict filters, no fallback, high thresholds.</summary>
    Precise,
    /// <summary>Balanced mode with moderate filters, fallback enabled.</summary>
    Balanced,
    /// <summary>Broad mode with loose filters, aggressive fallback.</summary>
    Broad
}

/// <summary>
/// Recall ranking preset configuration.
/// </summary>
/// <param name="SemanticWeight">Weight for semantic similarity score.</param>
/// <param name="LexicalWeight">Weight for BM25/lexical score.</param>
/// <param name="RecencyWeight">Weight for recency boost.</param>
/// <param name="TagWeight">Weight for tag match boost.</param>
/// <param name="MinSemanticScore">Minimum semantic score threshold.</param>
/// <param name="EnableFallback">Whether to enable semantic fallback.</param>
/// <param name="MinKeywordOverlap">Minimum keyword overlap required.</param>
public sealed record RecallPreset(
    double SemanticWeight,
    double LexicalWeight,
    double RecencyWeight,
    double TagWeight,
    double MinSemanticScore,
    bool EnableFallback,
    int MinKeywordOverlap);

/// <summary>
/// Static preset configurations for different recall modes.
/// </summary>
public static class RecallPresets
{
    /// <summary>Precise preset for hook usage - strict filtering, high confidence.</summary>
    public static readonly RecallPreset Precise = new(0.7, 0.25, 0.03, 0.02, 0.4, false, 2);

    /// <summary>Balanced preset for general use - moderate filtering.</summary>
    public static readonly RecallPreset Balanced = new(0.5, 0.35, 0.08, 0.07, 0.2, true, 1);

    /// <summary>Broad preset for maximum recall - loose filtering.</summary>
    public static readonly RecallPreset Broad = new(0.3, 0.5, 0.12, 0.08, 0.1, true, 0);

    /// <summary>Gets the preset configuration for a given mode.</summary>
    /// <param name="mode">The recall mode.</param>
    /// <returns>The corresponding preset configuration.</returns>
    public static RecallPreset GetPreset(MemoryRecallMode mode) => mode switch
    {
        MemoryRecallMode.Precise => Precise,
        MemoryRecallMode.Balanced => Balanced,
        MemoryRecallMode.Broad => Broad,
        _ => Balanced
    };
}

/// <summary>
/// Represents a single indexed chunk looked up by primary key.
/// </summary>
/// <param name="ChunkId">Chunk row identifier.</param>
/// <param name="SourcePath">Source path of the parent indexed document.</param>
/// <param name="ChunkIndex">Chunk ordinal within the source document.</param>
/// <param name="ChunkText">Stored chunk text.</param>
/// <param name="TokenCount">Stored token count estimate for the chunk.</param>
/// <param name="IndexedAtUtc">UTC timestamp when the parent document was indexed.</param>
public sealed record ChunkRecord(long ChunkId, string SourcePath, int ChunkIndex, string ChunkText, int TokenCount, DateTimeOffset IndexedAtUtc);
