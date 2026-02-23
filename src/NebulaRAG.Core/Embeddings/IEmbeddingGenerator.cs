namespace NebulaRAG.Core.Embeddings;

/// <summary>
/// Generates vector embeddings from text content.
/// </summary>
public interface IEmbeddingGenerator
{
    /// <summary>
    /// Generates a vector embedding for the provided text.
    /// </summary>
    /// <param name="text">The text to embed.</param>
    /// <param name="dimensions">The target vector dimension count.</param>
    /// <returns>A float array representing the text embedding.</returns>
    float[] GenerateEmbedding(string text, int dimensions);
}
