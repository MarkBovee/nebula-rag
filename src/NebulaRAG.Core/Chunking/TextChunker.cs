namespace NebulaRAG.Core.Chunking;

/// <summary>
/// Splits text content into fixed-size chunks with optional overlap.
/// </summary>
public sealed class TextChunker
{
    /// <summary>
    /// Splits content into chunks of specified size with optional overlap.
    /// </summary>
    /// <param name="content">The text content to chunk.</param>
    /// <param name="chunkSize">Maximum characters per chunk (must be > 0).</param>
    /// <param name="overlap">Number of overlapping characters between chunks (0 <= overlap < chunkSize).</param>
    /// <returns>A read-only list of text chunks with their index and token counts.</returns>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if chunkSize or overlap parameters are invalid.</exception>
    public IReadOnlyList<TextChunk> Chunk(string content, int chunkSize, int overlap)
    {
        if (chunkSize <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkSize), "Chunk size must be greater than 0.");
        }

        if (overlap < 0 || overlap >= chunkSize)
        {
            throw new ArgumentOutOfRangeException(nameof(overlap), "Chunk overlap must be >= 0 and < chunk size.");
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            return [];
        }

        // Normalize line endings for consistent processing
        var normalizedContent = content.Replace("\r\n", "\n");
        var step = chunkSize - overlap;
        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

        // Slide a window across content with the calculated step size
        for (var start = 0; start < normalizedContent.Length; start += step)
        {
            var length = Math.Min(chunkSize, normalizedContent.Length - start);
            var chunkText = normalizedContent.Substring(start, length).Trim();

            if (chunkText.Length == 0)
            {
                continue;
            }

            chunks.Add(new TextChunk(chunkIndex, chunkText, CountTokens(chunkText)));
            chunkIndex++;

            if (start + length >= normalizedContent.Length)
            {
                break;
            }
        }

        return chunks;
    }

    /// <summary>
    /// Estimates token count using simple whitespace-based splitting.
    /// For development use; not a precise tokenizer.
    /// </summary>
    private static int CountTokens(string chunkText)
    {
        return chunkText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }
}

/// <summary>
/// Represents a chunk of text with its position and estimated token count.
/// </summary>
/// <param name="Index">The zero-based index of this chunk within its document.</param>
/// <param name="Text">The trimmed text content of this chunk.</param>
/// <param name="TokenCount">Estimated number of tokens in this chunk.</param>
public sealed record TextChunk(int Index, string Text, int TokenCount);
