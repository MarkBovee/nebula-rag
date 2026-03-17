using System.Security.Cryptography;
using System.Text;
using NebulaRAG.Core.Chunking;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Pathing;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Provides administrative operations for the RAG system.
/// </summary>
public sealed class RagManagementService
{
    private readonly PostgresRagStore _store;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RagSettings _settings;
    private readonly ILogger<RagManagementService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagManagementService"/> class.
    /// </summary>
    /// <param name="store">The PostgreSQL RAG store.</param>
    /// <param name="chunker">Chunker used when updating indexed document content.</param>
    /// <param name="embeddingGenerator">Embedding generator used for memory semantic search.</param>
    /// <param name="settings">Runtime settings for vector dimensions and retrieval defaults.</param>
    /// <param name="logger">The logger instance.</param>
    public RagManagementService(PostgresRagStore store, TextChunker chunker, IEmbeddingGenerator embeddingGenerator, RagSettings settings, ILogger<RagManagementService> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
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
    /// Gets project-level RAG aggregates used for project-first operational views.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Per-project RAG statistics.</returns>
    public async Task<IReadOnlyList<ProjectRagStats>> GetProjectRagStatsAsync(CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Retrieving project-level RAG stats");

        try
        {
            var projectStats = await _store.GetProjectRagStatsAsync(cancellationToken);
            _logger.LogDebug("Retrieved {ProjectCount} project-level RAG rows", projectStats.Count);
            return projectStats;
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to retrieve project-level RAG stats");
            throw new RagDatabaseException("Failed to retrieve project-level RAG stats", ex);
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
    /// Lists indexed documents with optional project and text filters.
    /// </summary>
    /// <param name="projectId">Optional project identifier.</param>
    /// <param name="searchText">Optional search text.</param>
    /// <param name="limit">Maximum number of rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Indexed document summary rows.</returns>
    public async Task<IReadOnlyList<IndexedDocumentRecord>> ListIndexedDocumentsAsync(string? projectId, string? searchText, int limit = 300, CancellationToken cancellationToken = default)
    {
        _logger.LogDebug("Listing indexed documents (projectId={ProjectId}, searchText={SearchText}, limit={Limit})", projectId, searchText, limit);

        try
        {
            return await _store.ListIndexedDocumentsAsync(projectId, searchText, limit, cancellationToken);
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to list indexed documents");
            throw new RagDatabaseException("Failed to list indexed documents", ex);
        }
    }

    /// <summary>
    /// Retrieves one indexed document with reconstructed content.
    /// </summary>
    /// <param name="sourcePath">Stored source path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Document detail when found; otherwise <c>null</c>.</returns>
    public async Task<IndexedDocumentDetail?> GetIndexedDocumentAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
        }

        try
        {
            return await _store.GetIndexedDocumentAsync(sourcePath.Trim(), cancellationToken);
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to load indexed document {SourcePath}", sourcePath);
            throw new RagDatabaseException($"Failed to load indexed document: {sourcePath}", ex);
        }
    }

    /// <summary>
    /// Rewrites the content of an indexed document by regenerating its chunks and embeddings.
    /// </summary>
    /// <param name="sourcePath">Stored source path or source key.</param>
    /// <param name="projectId">Optional explicit project id for source-key prefixing.</param>
    /// <param name="content">Replacement content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated document detail after the write.</returns>
    public async Task<IndexedDocumentDetail> UpdateIndexedDocumentAsync(string sourcePath, string? projectId, string content, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Document content cannot be null or empty.", nameof(content));
        }

        var chunks = _chunker.Chunk(content, _settings.Ingestion.ChunkSize, _settings.Ingestion.ChunkOverlap);
        if (chunks.Count == 0)
        {
            throw new InvalidOperationException("No indexable chunks were produced from the replacement document content.");
        }

        var chunkEmbeddings = chunks
            .Select(chunk => new ChunkEmbedding(
                chunk.Index,
                chunk.Text,
                chunk.TokenCount,
                _embeddingGenerator.GenerateEmbedding(chunk.Text, _settings.Ingestion.VectorDimensions)))
            .ToList();

        var contentHash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
        var normalizedSourcePath = SourcePathNormalizer.NormalizeForStorage(sourcePath.Trim(), Directory.GetCurrentDirectory());
        var storedSourcePath = SourcePathNormalizer.ApplyExplicitProjectPrefix(normalizedSourcePath, projectId);

        try
        {
            await _store.UpsertDocumentAsync(storedSourcePath, contentHash, chunkEmbeddings, cancellationToken);
            return await _store.GetIndexedDocumentAsync(storedSourcePath, cancellationToken)
                ?? throw new InvalidOperationException("Indexed document could not be reloaded after update.");
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to update indexed document {SourcePath}", storedSourcePath);
            throw new RagDatabaseException($"Failed to update indexed document: {storedSourcePath}", ex);
        }
    }

    /// <summary>
    /// Deletes all indexed documents for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of deleted documents.</returns>
    public async Task<int> DeleteIndexedDocumentsByProjectAsync(string projectId, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectId))
        {
            throw new ArgumentException("Project id cannot be null or empty.", nameof(projectId));
        }

        try
        {
            return await _store.DeleteDocumentsByProjectAsync(projectId.Trim(), cancellationToken);
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Failed to delete indexed documents for project {ProjectId}", projectId);
            throw new RagDatabaseException($"Failed to delete indexed documents for project: {projectId}", ex);
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
