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
/// Represents a single indexed chunk looked up by primary key.
/// </summary>
/// <param name="ChunkId">Chunk row identifier.</param>
/// <param name="SourcePath">Source path of the parent indexed document.</param>
/// <param name="ChunkIndex">Chunk ordinal within the source document.</param>
/// <param name="ChunkText">Stored chunk text.</param>
/// <param name="TokenCount">Stored token count estimate for the chunk.</param>
/// <param name="IndexedAtUtc">UTC timestamp when the parent document was indexed.</param>
public sealed record ChunkRecord(long ChunkId, string SourcePath, int ChunkIndex, string ChunkText, int TokenCount, DateTimeOffset IndexedAtUtc);
