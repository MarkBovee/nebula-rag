using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Embeddings;

namespace NebulaRAG.Tests;

public class TextChunkerAndEmbeddingTests
{
    /// <summary>
    /// Validates that the TextChunker produces multiple chunks for a long input text and that each chunk contains tokens.
    /// </summary>
    [Fact]
    public void Chunker_ProducesMultipleChunks_ForLongText()
    {
        var chunker = new TextChunker();
        var text = string.Join(' ', Enumerable.Repeat("nebula", 500));

        var chunks = chunker.Chunk(text, chunkSize: 100, overlap: 20);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.TokenCount > 0));
    }

    /// <summary>
    /// Validates that the HashEmbeddingGenerator produces an embedding vector of the expected dimensions and that the vector is normalized (magnitude close to 1).
    /// </summary>
    [Fact]
    public void HashEmbeddingGenerator_ReturnsExpectedDimensions_AndNormalizedVector()
    {
        var generator = new HashEmbeddingGenerator();
        var embedding = generator.GenerateEmbedding("copilot rag postgres", 64);

        Assert.Equal(64, embedding.Length);
        var magnitude = Math.Sqrt(embedding.Sum(value => value * value));
        Assert.InRange(magnitude, 0.99, 1.01);
    }
}
