namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents aggregate index statistics for the RAG store.
/// </summary>
/// <param name="DocumentCount">Total number of indexed source documents.</param>
/// <param name="ChunkCount">Total number of indexed chunks.</param>
/// <param name="LatestIndexedAtUtc">UTC timestamp of the most recent indexing operation, if any.</param>
public sealed record RagIndexStats(long DocumentCount, long ChunkCount, DateTimeOffset? LatestIndexedAtUtc);
