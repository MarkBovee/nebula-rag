using Microsoft.Extensions.Logging;
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
    private readonly ILogger<RagManagementService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagManagementService"/> class.
    /// </summary>
    /// <param name="store">The PostgreSQL RAG store.</param>
    /// <param name="logger">The logger instance.</param>
    public RagManagementService(PostgresRagStore store, ILogger<RagManagementService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
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
}
