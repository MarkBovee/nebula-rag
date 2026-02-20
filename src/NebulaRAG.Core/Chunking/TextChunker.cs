namespace NebulaRAG.Core.Chunking;

public sealed class TextChunker
{
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

        var normalizedContent = content.Replace("\r\n", "\n");
        var step = chunkSize - overlap;
        var chunks = new List<TextChunk>();
        var chunkIndex = 0;

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

    private static int CountTokens(string chunkText)
    {
        return chunkText
            .Split(' ', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Length;
    }
}

public sealed record TextChunk(int Index, string Text, int TokenCount);
