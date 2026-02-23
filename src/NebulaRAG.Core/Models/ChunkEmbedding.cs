namespace NebulaRAG.Core.Models;

/// <summary>
/// Represents a text chunk paired with its generated vector embedding.
/// </summary>
/// <param name="ChunkIndex">Zero-based position of this chunk within its document.</param>
/// <param name="ChunkText">The text content of this chunk.</param>
/// <param name="TokenCount">Estimated number of tokens in the chunk.</param>
/// <param name="Embedding">The vector embedding (float array) for this chunk.</param>
public sealed record ChunkEmbedding(
    int ChunkIndex,
    string ChunkText,
    int TokenCount,
    float[] Embedding);
