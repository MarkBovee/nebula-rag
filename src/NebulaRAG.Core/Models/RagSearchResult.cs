namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents a single search result from a semantic query.
/// </summary>
/// <param name="SourcePath">The indexed source file path.</param>
/// <param name="ChunkIndex">The chunk index within the source document.</param>
/// <param name="ChunkText">The text content of the matching chunk.</param>
/// <param name="Score">Final blended ranking score.</param>
/// <param name="SemanticScore">Cosine similarity score (0.0 to 1.0, higher is more relevant).</param>
/// <param name="LexicalScore">BM25/lexical search score.</param>
/// <param name="RecencyBoost">Recency-based boost factor.</param>
/// <param name="TagBoost">Tag-based boost factor.</param>
/// <param name="UsedFallback">Whether fallback ranking was used.</param>
public sealed record RagSearchResult(
    string SourcePath,
    int ChunkIndex,
    string ChunkText,
    double Score,
    double? SemanticScore = null,
    double? LexicalScore = null,
    double? RecencyBoost = null,
    double? TagBoost = null,
    bool UsedFallback = false);
