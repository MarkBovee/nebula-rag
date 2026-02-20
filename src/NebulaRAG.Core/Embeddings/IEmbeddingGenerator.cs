namespace NebulaRAG.Core.Embeddings;

public interface IEmbeddingGenerator
{
    float[] GenerateEmbedding(string text, int dimensions);
}
