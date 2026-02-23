namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents a single search result from a semantic query.
/// </summary>
/// <param name="SourcePath">The indexed source file path.</param>
/// <param name="ChunkIndex">The chunk index within the source document.</param>
/// <param name="ChunkText">The text content of the matching chunk.</param>
/// <param name="Score">Cosine similarity score (0.0 to 1.0, higher is more relevant).</param>
public sealed record RagSearchResult(
    string SourcePath,
    int ChunkIndex,
    string ChunkText,
    double Score);
