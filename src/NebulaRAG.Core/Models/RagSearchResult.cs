namespace NebulaRAG.Core.Models;

public sealed record RagSearchResult(
    string SourcePath,
    int ChunkIndex,
    string ChunkText,
    double Score);
