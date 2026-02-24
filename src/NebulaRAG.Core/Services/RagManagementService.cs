using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Provides administrative operations for the RAG system.
/// </summary>
public sealed class RagManagementService
{
    private readonly PostgresRagStore _store;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RagSettings _settings;
    private readonly ILogger<RagManagementService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagManagementService"/> class.
    /// </summary>
    /// <param name="store">The PostgreSQL RAG store.</param>
    /// <param name="embeddingGenerator">Embedding generator used for memory semantic search.</param>
    /// <param name="settings">Runtime settings for vector dimensions and retrieval defaults.</param>
    /// <param name="logger">The logger instance.</param>
    public RagManagementService(PostgresRagStore store, IEmbeddingGenerator embeddingGenerator, RagSettings settings, ILogger<RagManagementService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Gets current index statistics.
    /// </summary>
    /// <param name="includeIndexSize">When true, includes relation size bytes in the returned stats.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Current index statistics snapshot.</returns>
    public async Task<IndexStats> GetStatsAsync(bool includeIndexSize = false, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving index statistics (includeIndexSize={IncludeIndexSize})", includeIndexSize);
        
        try
        {
            var stats = await _store.GetIndexStatsAsync(includeIndexSize, cancellationToken);
            _logger.LogDebug(
                "Index stats retrieved: {DocumentCount} documents, {ChunkCount} chunks, {TotalTokens} tokens",
                stats.DocumentCount,
                stats.ChunkCount,
                stats.TotalTokens);
            return stats;
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to retrieve index statistics");
            throw new RagDatabaseException("Failed to retrieve index statistics", ex);
        }
    }

    /// <summary>
    /// Gets all indexed document sources.
    /// </summary>
    /// <param name="limit">Maximum number of sources to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>List of indexed sources ordered by latest index time.</returns>
    public async Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0.");
        }

        _logger.LogDebug("Listing indexed sources (limit={Limit})", limit);
        
        try
        {
            var sources = await _store.ListSourcesAsync(limit, cancellationToken);
            _logger.LogDebug("Found {SourceCount} indexed sources", sources.Count);
            return sources;
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to list sources");
            throw new RagDatabaseException("Failed to list sources", ex);
        }
    }

    /// <summary>
    /// Deletes all chunks for a specific document source.
    /// </summary>
    /// <param name="sourcePath">The source path to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of chunks deleted.</returns>
    public async Task<int> DeleteSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
        }

        _logger.LogWarning("Deleting source: {SourcePath}", sourcePath);
        
        try
        {
            var deletedCount = await _store.DeleteSourceAsync(sourcePath, cancellationToken);
            _logger.LogWarning(
                "Deleted {Count} documents from source: {SourcePath}",
                deletedCount,
                sourcePath);
            return deletedCount;
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to delete source: {SourcePath}", sourcePath);
            throw new RagDatabaseException($"Failed to delete source: {sourcePath}", ex);
        }
    }

    /// <summary>
    /// Clears the entire RAG index (documents and chunks).
    /// </summary>
    public async Task PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogWarning("PURGING ENTIRE RAG INDEX - all data will be deleted");
        
        try
        {
            await _store.PurgeAllAsync(cancellationToken);
            _logger.LogWarning("RAG index purged successfully - all documents and chunks deleted");
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to purge index");
            throw new RagDatabaseException("Failed to purge RAG index", ex);
        }
    }

    /// <summary>
    /// Checks database connectivity and readiness.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Health check result with status and message.</returns>
    public async Task<HealthCheckResult> HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Running health check");
        
        try
        {
            await _store.HealthCheckAsync(cancellationToken);
            var message = "Database connection successful";
            _logger.LogDebug("Health check passed: {Message}", message);
            return new HealthCheckResult(IsHealthy: true, Message: message);
        }
        catch (Exception ex)
        {
            var message = $"Database error: {ex.Message}";
            _logger.LogError(ex, "Health check failed: {Message}", message);
            return new HealthCheckResult(IsHealthy: false, Message: message);
        }
    }

    /// <summary>
    /// Gets aggregated memory analytics for operational dashboards.
    /// </summary>
    /// <param name="dayWindow">Trailing number of days to include in daily memory counts.</param>
    /// <param name="topTagLimit">Maximum number of top tags to return.</param>
    /// <param name="recentSessionLimit">Maximum number of recent sessions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated memory analytics snapshot.</returns>
    public async Task<MemoryDashboardStats> GetMemoryStatsAsync(int dayWindow = 30, int topTagLimit = 10, int recentSessionLimit = 12, string? sessionId = null, string? projectId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Retrieving memory stats (dayWindow={DayWindow}, topTagLimit={TopTagLimit}, recentSessionLimit={RecentSessionLimit}, sessionId={SessionId}, projectId={ProjectId})",
            dayWindow,
            topTagLimit,
            recentSessionLimit,
            sessionId,
            projectId);

        try
        {
            var stats = await _store.GetMemoryStatsAsync(dayWindow, topTagLimit, recentSessionLimit, sessionId, projectId, cancellationToken);
            _logger.LogDebug(
                "Memory stats retrieved: {TotalMemories} memories, {Recent24HoursCount} in last 24h",
                stats.TotalMemories,
                stats.Recent24HoursCount);
            return stats;
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to retrieve memory stats");
            throw new RagDatabaseException("Failed to retrieve memory stats", ex);
        }
    }

    /// <summary>
    /// Lists memory records with optional type/tag/session/project filters.
    /// </summary>
    /// <param name="limit">Maximum number of memories to return.</param>
    /// <param name="type">Optional memory type filter.</param>
    /// <param name="tag">Optional tag filter.</param>
    /// <param name="sessionId">Optional session-id filter.</param>
    /// <param name="projectId">Optional project-id filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent memory records.</returns>
    public async Task<IReadOnlyList<MemoryRecord>> ListMemoriesAsync(int limit, string? type = null, string? tag = null, string? sessionId = null, string? projectId = null, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug(
            "Listing memories (limit={Limit}, type={Type}, tag={Tag}, sessionId={SessionId}, projectId={ProjectId})",
            limit,
            type,
            tag,
            sessionId,
            projectId);

        try
        {
            return await _store.ListMemoriesAsync(limit, type, tag, sessionId, projectId, cancellationToken);
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to list memories");
            throw new RagDatabaseException("Failed to list memories", ex);
        }
    }

    /// <summary>
    /// Performs semantic search over memories with optional type/tag/session/project filters.
    /// </summary>
    /// <param name="text">Natural language search text.</param>
    /// <param name="limit">Maximum number of memories to return.</param>
    /// <param name="type">Optional memory type filter.</param>
    /// <param name="tag">Optional tag filter.</param>
    /// <param name="sessionId">Optional session-id filter.</param>
    /// <param name="projectId">Optional project-id filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Semantically ranked memory results.</returns>
    public async Task<IReadOnlyList<MemorySearchResult>> SearchMemoriesAsync(string text, int limit, string? type = null, string? tag = null, string? sessionId = null, string? projectId = null, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(text))
        {
            throw new ArgumentException("Search text cannot be null or empty.", nameof(text));
        }

        _logger.LogDebug(
            "Searching memories (limit={Limit}, type={Type}, tag={Tag}, sessionId={SessionId}, projectId={ProjectId})",
            limit,
            type,
            tag,
            sessionId,
            projectId);

        try
        {
            var queryEmbedding = _embeddingGenerator.GenerateEmbedding(text, _settings.Ingestion.VectorDimensions);
            return await _store.SearchMemoriesAsync(queryEmbedding, limit, type, tag, sessionId, projectId, cancellationToken);
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to search memories");
            throw new RagDatabaseException("Failed to search memories", ex);
        }
    }
}
