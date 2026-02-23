using System.Diagnostics;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Executes semantic searches against the RAG index.
/// </summary>
public sealed class RagQueryService
{
    private readonly PostgresRagStore _store;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RagSettings _settings;
    private readonly ILogger<RagQueryService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagQueryService"/> class.
    /// </summary>
    public RagQueryService(
        PostgresRagStore store,
        IEmbeddingGenerator embeddingGenerator,
        RagSettings settings,
        ILogger<RagQueryService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Executes a semantic search query.
    /// </summary>
    /// <param name="queryText">The search query text.</param>
    /// <param name="topK">Maximum number of results to return (uses default if not specified).</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of search results ranked by relevance.</returns>
    /// <exception cref="RagQueryException">Thrown if query execution fails.</exception>
    public async Task<IReadOnlyList<RagSearchResult>> QueryAsync(
        string queryText,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            throw new ArgumentException("Query text cannot be null or empty.", nameof(queryText));
        }

        var resultCount = topK ?? _settings.Retrieval.DefaultTopK;
        if (resultCount <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "topK must be greater than 0");
        }

        _logger.LogInformation("Executing query (topK={TopK}): {QueryText}", resultCount, queryText);

        try
        {
            var timer = Stopwatch.StartNew();

            var embedding = _embeddingGenerator.GenerateEmbedding(queryText, _settings.Ingestion.VectorDimensions);
            var results = await _store.SearchAsync(embedding, resultCount, cancellationToken);

            timer.Stop();

            if (results.Count == 0)
            {
                _logger.LogInformation("Query returned no results ({ElapsedMs}ms)", timer.ElapsedMilliseconds);
            }
            else
            {
                _logger.LogInformation("Query returned {ResultCount} results ({ElapsedMs}ms, top score: {TopScore:F4})",
                    results.Count,
                    timer.ElapsedMilliseconds,
                    results[0].Score);

                if (timer.ElapsedMilliseconds > 1000)
                {
                    _logger.LogWarning("Slow query detected ({ElapsedMs}ms)", timer.ElapsedMilliseconds);
                }
            }

            return results;
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Query execution failed: {QueryText}", queryText);
            throw new RagQueryException($"Query execution failed: {queryText}", ex);
        }
    }
}
