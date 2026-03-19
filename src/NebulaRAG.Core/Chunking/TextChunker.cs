namespace NebulaRAG.Core.Chunking;

/// <summary>
/// Splits text content into boundary-aware chunks with optional overlap.
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
        var chunks = new List<TextChunk>();

        if (normalizedContent.Length <= chunkSize)
        {
            return CreateSingleChunk(normalizedContent);
        }

        var chunkStartIndex = 0;
        while (chunkStartIndex < normalizedContent.Length)
        {
            var chunkEndIndex = FindChunkEnd(normalizedContent, chunkStartIndex, chunkSize);
            AppendChunk(chunks, normalizedContent, chunkStartIndex, chunkEndIndex);

            if (chunkEndIndex >= normalizedContent.Length)
            {
                break;
            }

            chunkStartIndex = FindNextChunkStart(normalizedContent, chunkStartIndex, chunkEndIndex, overlap);
        }

        return chunks;
    }

    /// <summary>
    /// Creates a single chunk for content that already fits within the configured size.
    /// </summary>
    /// <param name="normalizedContent">Normalized content string.</param>
    /// <returns>Single chunk preserving the trimmed content.</returns>
    private static IReadOnlyList<TextChunk> CreateSingleChunk(string normalizedContent)
    {
        var chunkText = normalizedContent.Trim();
        return chunkText.Length == 0
            ? []
            : [new TextChunk(0, chunkText, CountTokens(chunkText))];
    }

    /// <summary>
    /// Locates the best chunk end near the target size, preferring paragraph and line boundaries.
    /// </summary>
    /// <param name="content">Normalized content string.</param>
    /// <param name="startIndex">Current chunk start index.</param>
    /// <param name="chunkSize">Target maximum chunk size.</param>
    /// <returns>Chosen end index for the current chunk.</returns>
    private static int FindChunkEnd(string content, int startIndex, int chunkSize)
    {
        var idealEndIndex = Math.Min(startIndex + chunkSize, content.Length);
        if (idealEndIndex >= content.Length)
        {
            return content.Length;
        }

        var minimumEndIndex = Math.Min(content.Length, startIndex + Math.Max(chunkSize / 2, 1));
        return FindPreferredBoundary(content, minimumEndIndex, idealEndIndex)
            ?? idealEndIndex;
    }

    /// <summary>
    /// Searches for a paragraph, line, or whitespace boundary within the preferred chunk window.
    /// </summary>
    /// <param name="content">Normalized content string.</param>
    /// <param name="minimumEndIndex">Earliest acceptable boundary index.</param>
    /// <param name="idealEndIndex">Ideal boundary index near the chunk-size limit.</param>
    /// <returns>Boundary index when one is found; otherwise <c>null</c>.</returns>
    private static int? FindPreferredBoundary(string content, int minimumEndIndex, int idealEndIndex)
    {
        return FindBoundary(content, "\n\n", minimumEndIndex, idealEndIndex)
            ?? FindBoundary(content, "\n", minimumEndIndex, idealEndIndex)
            ?? FindWhitespaceBoundary(content, minimumEndIndex, idealEndIndex);
    }

    /// <summary>
    /// Searches backward for a specific delimiter and returns the index immediately after it.
    /// </summary>
    /// <param name="content">Normalized content string.</param>
    /// <param name="delimiter">Preferred boundary delimiter.</param>
    /// <param name="minimumEndIndex">Earliest acceptable boundary index.</param>
    /// <param name="idealEndIndex">Latest preferred boundary index.</param>
    /// <returns>Boundary index when the delimiter is found; otherwise <c>null</c>.</returns>
    private static int? FindBoundary(string content, string delimiter, int minimumEndIndex, int idealEndIndex)
    {
        var searchLength = idealEndIndex - minimumEndIndex;
        if (searchLength <= 0)
        {
            return null;
        }

        var boundaryIndex = content.LastIndexOf(delimiter, idealEndIndex - 1, searchLength, StringComparison.Ordinal);
        return boundaryIndex >= minimumEndIndex ? boundaryIndex + delimiter.Length : null;
    }

    /// <summary>
    /// Searches backward for whitespace to avoid cutting a chunk in the middle of a word.
    /// </summary>
    /// <param name="content">Normalized content string.</param>
    /// <param name="minimumEndIndex">Earliest acceptable boundary index.</param>
    /// <param name="idealEndIndex">Latest preferred boundary index.</param>
    /// <returns>Whitespace boundary index when found; otherwise <c>null</c>.</returns>
    private static int? FindWhitespaceBoundary(string content, int minimumEndIndex, int idealEndIndex)
    {
        for (var candidateIndex = idealEndIndex - 1; candidateIndex >= minimumEndIndex; candidateIndex--)
        {
            if (char.IsWhiteSpace(content[candidateIndex]))
            {
                return candidateIndex + 1;
            }
        }

        return null;
    }

    /// <summary>
    /// Appends a chunk slice when the trimmed content is non-empty.
    /// </summary>
    /// <param name="chunks">Destination chunk list.</param>
    /// <param name="content">Normalized content string.</param>
    /// <param name="startIndex">Chunk start index.</param>
    /// <param name="endIndex">Chunk end index.</param>
    private static void AppendChunk(List<TextChunk> chunks, string content, int startIndex, int endIndex)
    {
        var chunkText = content[startIndex..endIndex].Trim();
        if (chunkText.Length == 0)
        {
            return;
        }

        chunks.Add(new TextChunk(chunks.Count, chunkText, CountTokens(chunkText)));
    }

    /// <summary>
    /// Computes the next chunk start index and skips leading whitespace-only overlap.
    /// </summary>
    /// <param name="content">Normalized content string.</param>
    /// <param name="currentStartIndex">Current chunk start index.</param>
    /// <param name="currentEndIndex">Current chunk end index.</param>
    /// <param name="overlap">Configured overlap size.</param>
    /// <returns>Next chunk start index.</returns>
    private static int FindNextChunkStart(string content, int currentStartIndex, int currentEndIndex, int overlap)
    {
        var nextStartIndex = Math.Max(currentStartIndex + 1, currentEndIndex - overlap);
        while (nextStartIndex < content.Length && char.IsWhiteSpace(content[nextStartIndex]))
        {
            nextStartIndex++;
        }

        return nextStartIndex;
    }

    /// <summary>
    /// Estimates token count using simple whitespace-based splitting.
    /// For development use; not a precise tokenizer.
    /// </summary>
    /// <param name="chunkText">Chunk text to tokenize approximately.</param>
    /// <returns>Whitespace-delimited token count estimate.</returns>
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
