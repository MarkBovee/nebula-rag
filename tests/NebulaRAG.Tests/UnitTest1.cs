using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Embeddings;

namespace NebulaRAG.Tests;

public class UnitTest1
{
    [Fact]
    public void Chunker_ProducesMultipleChunks_ForLongText()
    {
        var chunker = new TextChunker();
        var text = string.Join(' ', Enumerable.Repeat("nebula", 500));

        var chunks = chunker.Chunk(text, chunkSize: 100, overlap: 20);

        Assert.True(chunks.Count > 1);
        Assert.All(chunks, chunk => Assert.True(chunk.TokenCount > 0));
    }

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
