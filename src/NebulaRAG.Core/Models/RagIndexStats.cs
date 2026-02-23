namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents aggregate index statistics for the RAG store.
/// </summary>
/// <param name="DocumentCount">Total number of indexed source documents.</param>
/// <param name="ChunkCount">Total number of indexed chunks.</param>
/// <param name="TotalTokens">Total estimated tokens across all chunks.</param>
/// <param name="OldestIndexedAt">UTC timestamp of the earliest indexing operation, if any.</param>
/// <param name="NewestIndexedAt">UTC timestamp of the most recent indexing operation, if any.</param>
public sealed record RagIndexStats(
    long DocumentCount,
    long ChunkCount,
    long TotalTokens,
    DateTime? OldestIndexedAt,
    DateTime? NewestIndexedAt);
