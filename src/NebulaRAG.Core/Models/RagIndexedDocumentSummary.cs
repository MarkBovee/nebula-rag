namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents an indexed document summary entry ordered by recency.
/// </summary>
/// <param name="SourcePath">The indexed source path or file name.</param>
/// <param name="IndexedAtUtc">UTC timestamp of the latest index operation for this source.</param>
/// <param name="ChunkCount">Number of chunks currently stored for this source in the index.</param>
public sealed record RagIndexedDocumentSummary(
    string SourcePath,
    DateTimeOffset IndexedAtUtc,
    int ChunkCount);
