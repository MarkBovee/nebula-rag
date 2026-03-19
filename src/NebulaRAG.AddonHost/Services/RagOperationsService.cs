using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;

namespace NebulaRAG.AddonHost.Services;

/// <summary>
/// Provides a focused façade for dashboard RAG operations.
/// </summary>
public sealed class RagOperationsService
{
    private readonly DashboardSnapshotService _snapshotService;
    private readonly RagQueryService _queryService;
    private readonly RagManagementService _managementService;
    private readonly RagIndexer _indexer;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagOperationsService"/> class.
    /// </summary>
    /// <param name="snapshotService">Dashboard snapshot source.</param>
    /// <param name="queryService">Semantic query service.</param>
    /// <param name="managementService">Index management service.</param>
    /// <param name="indexer">Filesystem indexing service.</param>
    public RagOperationsService(DashboardSnapshotService snapshotService, RagQueryService queryService, RagManagementService managementService, RagIndexer indexer)
    {
        _snapshotService = snapshotService ?? throw new ArgumentNullException(nameof(snapshotService));
        _queryService = queryService ?? throw new ArgumentNullException(nameof(queryService));
        _managementService = managementService ?? throw new ArgumentNullException(nameof(managementService));
        _indexer = indexer ?? throw new ArgumentNullException(nameof(indexer));
    }

    /// <summary>
    /// Queries indexed content using semantic search.
    /// </summary>
    /// <param name="queryText">Query text.</param>
    /// <param name="limit">Result limit.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Ranked search matches.</returns>
    public Task<IReadOnlyList<RagSearchResult>> QueryAsync(string queryText, int limit, CancellationToken cancellationToken = default)
    {
        return _queryService.QueryAsync(queryText, Math.Clamp(limit, 1, 20), cancellationToken);
    }

    /// <summary>
    /// Indexes content from a filesystem source path.
    /// </summary>
    /// <param name="sourcePath">Directory path to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Indexing summary.</returns>
    public Task<IndexSummary> IndexPathAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return _indexer.IndexDirectoryAsync(sourcePath, cancellationToken: cancellationToken);
    }

    /// <summary>
    /// Deletes one indexed source path.
    /// </summary>
    /// <param name="sourcePath">Indexed source path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Deleted row count.</returns>
    public Task<int> DeleteSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return _managementService.DeleteSourceAsync(sourcePath, cancellationToken);
    }

    /// <summary>
    /// Purges all indexed content.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Completion task.</returns>
    public Task PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        return _managementService.PurgeAllAsync(cancellationToken);
    }

    /// <summary>
    /// Lists indexed sources from the dashboard snapshot.
    /// </summary>
    /// <param name="limit">Maximum number of sources to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Source list ordered by index timestamp.</returns>
    public async Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(int limit, CancellationToken cancellationToken = default)
    {
        var snapshot = await _snapshotService.GetDashboardAsync(cancellationToken);
        return snapshot.Sources.Take(Math.Clamp(limit, 1, 500)).ToList();
    }

    /// <summary>
    /// Lists indexed documents with optional project and text filters.
    /// </summary>
    /// <param name="projectId">Optional project identifier.</param>
    /// <param name="searchText">Optional text filter.</param>
    /// <param name="limit">Maximum number of rows to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Indexed document summary rows.</returns>
    public Task<IReadOnlyList<IndexedDocumentRecord>> ListDocumentsAsync(string? projectId, string? searchText, int limit, CancellationToken cancellationToken = default)
    {
        return _managementService.ListIndexedDocumentsAsync(projectId, searchText, Math.Clamp(limit, 1, 500), cancellationToken);
    }

    /// <summary>
    /// Loads one indexed document for viewing or editing.
    /// </summary>
    /// <param name="sourcePath">Stored source path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Indexed document detail when found.</returns>
    public Task<IndexedDocumentDetail?> GetDocumentAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        return _managementService.GetIndexedDocumentAsync(sourcePath, cancellationToken);
    }

    /// <summary>
    /// Updates one indexed document by regenerating chunks and embeddings from replacement content.
    /// </summary>
    /// <param name="sourcePath">Stored source path.</param>
    /// <param name="projectId">Optional project identifier.</param>
    /// <param name="content">Replacement document content.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Updated document detail.</returns>
    public Task<IndexedDocumentDetail> UpdateDocumentAsync(string sourcePath, string? projectId, string content, CancellationToken cancellationToken = default)
    {
        return _managementService.UpdateIndexedDocumentAsync(sourcePath, projectId, content, cancellationToken);
    }

    /// <summary>
    /// Deletes all indexed documents for a project.
    /// </summary>
    /// <param name="projectId">Project identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of deleted documents.</returns>
    public Task<int> DeleteProjectDocumentsAsync(string projectId, CancellationToken cancellationToken = default)
    {
        return _managementService.DeleteIndexedDocumentsByProjectAsync(projectId, cancellationToken);
    }
}
