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
        Assert.All(chunks, chunk => Assert.True(chunk.Text.Length <= 100));
    }

    /// <summary>
    /// Validates that the TextChunker prefers paragraph boundaries when they fit within the chunk size budget.
    /// </summary>
    [Fact]
    public void Chunker_PrefersParagraphBoundaries_WhenAvailable()
    {
        var chunker = new TextChunker();
        var text = "Alpha paragraph keeps together.\n\nBeta paragraph also stays together.\n\nGamma paragraph finishes the sample.";

        var chunks = chunker.Chunk(text, chunkSize: 40, overlap: 0);

        Assert.True(chunks.Count >= 3);
        Assert.Equal("Alpha paragraph keeps together.", chunks[0].Text);
        Assert.Equal("Beta paragraph also stays together.", chunks[1].Text);
    }

    /// <summary>
    /// Validates that the TextChunker trims leading overlap whitespace when advancing between boundary-aware chunks.
    /// </summary>
    [Fact]
    public void Chunker_TrimsLeadingWhitespace_WhenOverlapAdvancesAcrossBoundaries()
    {
        var chunker = new TextChunker();
        var text = "Header section\n\nSecond section carries enough text to require another chunk boundary.";

        var chunks = chunker.Chunk(text, chunkSize: 35, overlap: 8);

        Assert.True(chunks.Count >= 2);
        Assert.False(chunks[1].Text.StartsWith("\n", StringComparison.Ordinal));
        Assert.False(chunks[1].Text.StartsWith(" ", StringComparison.Ordinal));
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
