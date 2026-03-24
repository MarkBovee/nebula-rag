using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>Storage abstraction used by AutoMemorySyncService (enables unit testing).</summary>
public interface IAutoMemoryStore
{
    /// <summary>Returns the sync state entry for the given file path, or <c>null</c> if not yet tracked.</summary>
    /// <param name="filePath">Absolute path of the file to look up.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<SyncStateEntry?> GetSyncStateAsync(string filePath, CancellationToken cancellationToken = default);

    /// <summary>Inserts or updates the sync state for a file with the given content hash.</summary>
    /// <param name="filePath">Absolute path of the file.</param>
    /// <param name="hash">SHA-256 hex hash of the file content at sync time.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task UpsertSyncStateAsync(string filePath, string hash, CancellationToken cancellationToken = default);

    /// <summary>Returns all sync state entries whose last-synced timestamp is older than <paramref name="cutoff"/>.</summary>
    /// <param name="cutoff">Entries synced before this UTC timestamp are considered stale.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SyncStateEntry>> ListStaleSyncEntriesAsync(DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>Deletes memory rows whose tags match the given prefix and that were created before <paramref name="cutoff"/>.</summary>
    /// <param name="tagPrefix">Tag prefix to match (e.g. <c>auto-memory</c>).</param>
    /// <param name="cutoff">Memories created before this UTC timestamp are eligible for deletion.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of deleted memory rows.</returns>
    Task<int> DeleteMemoriesByTagOlderThanAsync(string tagPrefix, DateTimeOffset cutoff, CancellationToken cancellationToken = default);

    /// <summary>Returns a list of indexed RAG document sources, up to <paramref name="limit"/> entries.</summary>
    /// <param name="limit">Maximum number of sources to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(int limit = 100, CancellationToken cancellationToken = default);
}
