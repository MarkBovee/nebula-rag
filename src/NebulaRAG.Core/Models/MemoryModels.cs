namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents a stored memory record with metadata.
/// </summary>
/// <param name="Id">Database identifier of the memory entry.</param>
/// <param name="SessionId">Logical session identifier associated with the memory.</param>
/// <param name="Type">Memory type: episodic, semantic, or procedural.</param>
/// <param name="Content">Natural language memory content.</param>
/// <param name="Tags">Tags associated with the memory for filtering.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the memory was created.</param>
public sealed record MemoryRecord(long Id, string SessionId, string Type, string Content, IReadOnlyList<string> Tags, DateTimeOffset CreatedAtUtc);

/// <summary>
/// Represents a memory recall result including semantic score.
/// </summary>
/// <param name="Id">Database identifier of the recalled memory entry.</param>
/// <param name="SessionId">Logical session identifier associated with the memory.</param>
/// <param name="Type">Memory type: episodic, semantic, or procedural.</param>
/// <param name="Content">Natural language memory content.</param>
/// <param name="Tags">Tags associated with the memory for filtering.</param>
/// <param name="CreatedAtUtc">UTC timestamp when the memory was created.</param>
/// <param name="Score">Cosine similarity score against the recall query.</param>
public sealed record MemorySearchResult(long Id, string SessionId, string Type, string Content, IReadOnlyList<string> Tags, DateTimeOffset CreatedAtUtc, double Score);

/// <summary>
/// Aggregated memory analytics payload used by dashboard and API consumers.
/// </summary>
/// <param name="TotalMemories">Total number of stored memories.</param>
/// <param name="Recent24HoursCount">Number of memories created in the last 24 hours.</param>
/// <param name="DistinctSessionCount">Number of distinct session identifiers represented in memory records.</param>
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
/// Represents a single indexed chunk looked up by primary key.
/// </summary>
/// <param name="ChunkId">Chunk row identifier.</param>
/// <param name="SourcePath">Source path of the parent indexed document.</param>
/// <param name="ChunkIndex">Chunk ordinal within the source document.</param>
/// <param name="ChunkText">Stored chunk text.</param>
/// <param name="TokenCount">Stored token count estimate for the chunk.</param>
/// <param name="IndexedAtUtc">UTC timestamp when the parent document was indexed.</param>
public sealed record ChunkRecord(long ChunkId, string SourcePath, int ChunkIndex, string ChunkText, int TokenCount, DateTimeOffset IndexedAtUtc);
