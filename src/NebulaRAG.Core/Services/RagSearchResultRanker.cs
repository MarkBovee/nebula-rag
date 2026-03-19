using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Applies low-risk ranking rules for blending semantic and lexical retrieval results.
/// </summary>
internal static class RagSearchResultRanker
{
    private const double SemanticFallbackScoreThreshold = 0.35d;
    private const double ExactSourcePathBoost = 2.5d;
    private const double ExactFileNameBoost = 2.0d;
    private const double SourcePathTermBoost = 0.5d;
    private const double ExactChunkTextBoost = 1.0d;
    private const double ChunkTextTermBoost = 0.15d;

    /// <summary>
    /// Determines whether lexical fallback should be used to supplement semantic retrieval.
    /// </summary>
    /// <param name="semanticResults">Results returned from semantic search.</param>
    /// <param name="requestedCount">Requested maximum result count.</param>
    /// <returns><c>true</c> when lexical fallback should be attempted.</returns>
    public static bool ShouldUseLexicalFallback(IReadOnlyList<RagSearchResult> semanticResults, int requestedCount)
    {
        ArgumentNullException.ThrowIfNull(semanticResults);

        if (requestedCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedCount), "requestedCount must be greater than 0.");
        }

        if (semanticResults.Count == 0)
        {
            return true;
        }

        if (semanticResults.Count < requestedCount)
        {
            return true;
        }

        return semanticResults[0].Score < SemanticFallbackScoreThreshold;
    }

    /// <summary>
    /// Merges semantic and lexical results, then boosts exact path and text matches for the query.
    /// </summary>
    /// <param name="queryText">Original user query text.</param>
    /// <param name="primaryResults">Primary semantic retrieval results.</param>
    /// <param name="fallbackResults">Lexical fallback retrieval results.</param>
    /// <param name="requestedCount">Maximum number of merged results to keep.</param>
    /// <returns>Merged list capped to the requested count.</returns>
    public static IReadOnlyList<RagSearchResult> MergePrimaryWithFallback(
        string queryText,
        IReadOnlyList<RagSearchResult> primaryResults,
        IReadOnlyList<RagSearchResult> fallbackResults,
        int requestedCount)
    {
        ArgumentException.ThrowIfNullOrWhiteSpace(queryText);
        ArgumentNullException.ThrowIfNull(primaryResults);
        ArgumentNullException.ThrowIfNull(fallbackResults);

        if (requestedCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(requestedCount), "requestedCount must be greater than 0.");
        }

        var queryTerms = ExtractQueryTerms(queryText);
        var mergedResults = new List<RagSearchResult>(primaryResults.Count + fallbackResults.Count);
        var seenKeys = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        AppendUnique(primaryResults, mergedResults, seenKeys);
        AppendUnique(fallbackResults, mergedResults, seenKeys);

        return mergedResults
            .OrderByDescending(result => CalculateBoostedScore(result, queryText, queryTerms))
            .ThenBy(result => result.SourcePath, StringComparer.OrdinalIgnoreCase)
            .ThenBy(result => result.ChunkIndex)
            .Take(requestedCount)
            .ToList();
    }

    /// <summary>
    /// Appends unique search results while preserving their original order.
    /// </summary>
    /// <param name="source">Results to inspect for insertion.</param>
    /// <param name="destination">Merged result buffer.</param>
    /// <param name="seenKeys">Case-insensitive key set used for deduplication.</param>
    private static void AppendUnique(
        IReadOnlyList<RagSearchResult> source,
        List<RagSearchResult> destination,
        HashSet<string> seenKeys)
    {
        foreach (var result in source)
        {
            var resultKey = BuildResultKey(result);
            if (!seenKeys.Add(resultKey))
            {
                continue;
            }

            destination.Add(result);
        }
    }

    /// <summary>
    /// Calculates a composite ranking score that favors exact filename, source-path, and text matches.
    /// </summary>
    /// <param name="result">Candidate result to score.</param>
    /// <param name="queryText">Original query text.</param>
    /// <param name="queryTerms">Normalized query terms for partial matching.</param>
    /// <returns>Composite score used for final ordering.</returns>
    private static double CalculateBoostedScore(RagSearchResult result, string queryText, IReadOnlyList<string> queryTerms)
    {
        var normalizedSourcePath = NormalizeComparisonValue(result.SourcePath);
        var normalizedQueryText = NormalizeComparisonValue(queryText);
        var normalizedFileName = NormalizeComparisonValue(Path.GetFileName(normalizedSourcePath));
        var normalizedChunkText = NormalizeComparisonValue(result.ChunkText);
        var score = result.Score;

        if (normalizedSourcePath.Equals(normalizedQueryText, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactSourcePathBoost;
        }

        if (normalizedFileName.Equals(normalizedQueryText, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactFileNameBoost;
        }

        if (normalizedChunkText.Equals(normalizedQueryText, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactChunkTextBoost;
        }

        score += CountMatchingTerms(normalizedSourcePath, queryTerms) * SourcePathTermBoost;
        score += CountMatchingTerms(normalizedChunkText, queryTerms) * ChunkTextTermBoost;
        return score;
    }

    /// <summary>
    /// Extracts normalized query terms suitable for source-path and text matching.
    /// </summary>
    /// <param name="queryText">Raw query text.</param>
    /// <returns>Distinct normalized terms in input order.</returns>
    private static IReadOnlyList<string> ExtractQueryTerms(string queryText)
    {
        return NormalizeComparisonValue(queryText)
            .Split([' ', '/', '\\', '.', '-', '_', ':'], StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Distinct(StringComparer.OrdinalIgnoreCase)
            .ToList();
    }

    /// <summary>
    /// Counts distinct query terms present in a candidate text value.
    /// </summary>
    /// <param name="candidateText">Normalized source or chunk text.</param>
    /// <param name="queryTerms">Normalized query terms.</param>
    /// <returns>Number of unique query terms found in the candidate text.</returns>
    private static int CountMatchingTerms(string candidateText, IReadOnlyList<string> queryTerms)
    {
        return queryTerms.Count(term => candidateText.Contains(term, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>
    /// Normalizes a comparison value for case-insensitive exact and partial matching.
    /// </summary>
    /// <param name="value">Raw value to normalize.</param>
    /// <returns>Lower-noise comparison string.</returns>
    private static string NormalizeComparisonValue(string value)
    {
        if (string.IsNullOrWhiteSpace(value))
        {
            return string.Empty;
        }

        return value.Trim().Replace('\\', '/').ToLowerInvariant();
    }

    /// <summary>
    /// Builds a stable deduplication key for a search result.
    /// </summary>
    /// <param name="result">Search result to identify.</param>
    /// <returns>Compound key of source path and chunk index.</returns>
    private static string BuildResultKey(RagSearchResult result)
    {
        return $"{result.SourcePath}::{result.ChunkIndex}";
    }
}