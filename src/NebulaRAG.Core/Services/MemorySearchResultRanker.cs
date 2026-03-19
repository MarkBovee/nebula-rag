using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Applies low-risk ranking rules for blending semantic and lexical memory recall results.
/// </summary>
internal static class MemorySearchResultRanker
{
    private const double SemanticFallbackScoreThreshold = 0.35d;
    private const double ExactContentBoost = 1.0d;
    private const double ExactTagBoost = 0.75d;
    private const double ContentTermBoost = 0.2d;
    private const double TagTermBoost = 0.35d;

    /// <summary>
    /// Determines whether lexical fallback should be used to supplement semantic memory recall.
    /// </summary>
    /// <param name="semanticResults">Results returned from semantic recall.</param>
    /// <param name="requestedCount">Requested maximum result count.</param>
    /// <returns><c>true</c> when lexical fallback should be attempted.</returns>
    public static bool ShouldUseLexicalFallback(IReadOnlyList<MemorySearchResult> semanticResults, int requestedCount)
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
    /// Merges semantic and lexical memory results, then boosts exact and partial content/tag matches.
    /// </summary>
    /// <param name="queryText">Original user query text.</param>
    /// <param name="primaryResults">Primary semantic recall results.</param>
    /// <param name="fallbackResults">Lexical fallback recall results.</param>
    /// <param name="requestedCount">Maximum number of merged results to keep.</param>
    /// <returns>Merged list capped to the requested count.</returns>
    public static IReadOnlyList<MemorySearchResult> MergePrimaryWithFallback(
        string queryText,
        IReadOnlyList<MemorySearchResult> primaryResults,
        IReadOnlyList<MemorySearchResult> fallbackResults,
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
        var mergedResults = new List<MemorySearchResult>(primaryResults.Count + fallbackResults.Count);
        var seenIds = new HashSet<long>();

        AppendUnique(primaryResults, mergedResults, seenIds);
        AppendUnique(fallbackResults, mergedResults, seenIds);

        return mergedResults
            .OrderByDescending(result => CalculateBoostedScore(result, queryText, queryTerms))
            .ThenByDescending(result => result.CreatedAtUtc)
            .ThenBy(result => result.Id)
            .Take(requestedCount)
            .ToList();
    }

    /// <summary>
    /// Appends unique memory search results while preserving their original order.
    /// </summary>
    /// <param name="source">Results to inspect for insertion.</param>
    /// <param name="destination">Merged result buffer.</param>
    /// <param name="seenIds">Identifier set used for deduplication.</param>
    private static void AppendUnique(
        IReadOnlyList<MemorySearchResult> source,
        List<MemorySearchResult> destination,
        HashSet<long> seenIds)
    {
        foreach (var result in source)
        {
            if (!seenIds.Add(result.Id))
            {
                continue;
            }

            destination.Add(result);
        }
    }

    /// <summary>
    /// Calculates a composite ranking score that favors exact content and tag matches.
    /// </summary>
    /// <param name="result">Candidate result to score.</param>
    /// <param name="queryText">Original query text.</param>
    /// <param name="queryTerms">Normalized query terms for partial matching.</param>
    /// <returns>Composite score used for final ordering.</returns>
    private static double CalculateBoostedScore(MemorySearchResult result, string queryText, IReadOnlyList<string> queryTerms)
    {
        var normalizedQueryText = NormalizeComparisonValue(queryText);
        var normalizedContent = NormalizeComparisonValue(result.Content);
        var normalizedTags = result.Tags
            .Select(NormalizeComparisonValue)
            .Where(static tag => !string.IsNullOrWhiteSpace(tag))
            .ToList();
        var score = result.Score;

        if (normalizedContent.Equals(normalizedQueryText, StringComparison.OrdinalIgnoreCase))
        {
            score += ExactContentBoost;
        }

        if (normalizedTags.Contains(normalizedQueryText, StringComparer.OrdinalIgnoreCase))
        {
            score += ExactTagBoost;
        }

        score += CountMatchingTerms(normalizedContent, queryTerms) * ContentTermBoost;
        score += CountMatchingTerms(string.Join(' ', normalizedTags), queryTerms) * TagTermBoost;
        return score;
    }

    /// <summary>
    /// Extracts normalized query terms suitable for content and tag matching.
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
    /// <param name="candidateText">Normalized content or tag text.</param>
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
}
