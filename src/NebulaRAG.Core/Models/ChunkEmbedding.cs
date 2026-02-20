namespace NebulaRAG.Core.Models;

public sealed record ChunkEmbedding(
    int ChunkIndex,
    string ChunkText,
    int TokenCount,
    float[] Embedding);
