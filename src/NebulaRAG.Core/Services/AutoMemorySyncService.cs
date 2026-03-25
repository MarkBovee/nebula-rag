using System.Diagnostics;
using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Models;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Implements three-phase sync: auto-memory bridge, stale memory pruning, dirty RAG reindex.
/// </summary>
public sealed class AutoMemorySyncService
{
    private readonly IAutoMemoryStore _store;
    private readonly IAutoMemoryIndexer _indexer;
    private readonly RagSettings _settings;
    private readonly ILogger<AutoMemorySyncService> _logger;

    public AutoMemorySyncService(
        IAutoMemoryStore store,
        IAutoMemoryIndexer indexer,
        RagSettings settings,
        ILogger<AutoMemorySyncService> logger)
    {
        _store = store;
        _indexer = indexer;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>Runs all three sync phases and returns a summary.</summary>
    public async Task<SyncSummary> SyncAsync(CancellationToken cancellationToken = default)
    {
        var sw = Stopwatch.StartNew();
        var errors = new List<string>();
        int filesIngested = 0, memoriesPruned = 0, sourcesReindexed = 0;

        // Phase 1: Auto-Memory Bridge
        try { filesIngested = await BridgeAutoMemoryAsync(errors, cancellationToken); }
        catch (Exception ex) { errors.Add($"Phase1 fatal: {ex.Message}"); }

        // Phase 2: Stale Memory Pruning
        try { memoriesPruned = await PruneStaleMemoriesAsync(cancellationToken); }
        catch (Exception ex) { errors.Add($"Phase2 fatal: {ex.Message}"); }

        // Phase 3: Dirty RAG Source Reindex
        try { sourcesReindexed = await ReindexDirtySourcesAsync(errors, cancellationToken); }
        catch (Exception ex) { errors.Add($"Phase3 fatal: {ex.Message}"); }

        sw.Stop();
        return new SyncSummary(filesIngested, memoriesPruned, sourcesReindexed, errors, sw.ElapsedMilliseconds);
    }

    private async Task<int> BridgeAutoMemoryAsync(List<string> errors, CancellationToken ct)
    {
        var baseDir = ResolveBaseDirectory(_settings.AutoMemory.BaseDirectory);
        if (!Directory.Exists(baseDir))
        {
            _logger.LogWarning("Auto-memory base directory not found: {Dir}. Phase 1 skipped.", baseDir);
            return 0;
        }

        var files = Directory.EnumerateFiles(baseDir, "*.md", SearchOption.AllDirectories)
                             .Where(f => f.Contains(Path.DirectorySeparatorChar + "memory" + Path.DirectorySeparatorChar))
                             .ToList();

        int ingested = 0;
        foreach (var file in files)
        {
            try
            {
                var content = await File.ReadAllTextAsync(file, ct);
                var hash = ComputeHash(content);
                var existing = await _store.GetSyncStateAsync(file, ct);
                if (existing?.LastHash == hash) continue;

                var slug = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(file))) ?? "unknown";
                await _indexer.IngestFileAsync(file, slug, ct);
                await _store.UpsertSyncStateAsync(file, hash, ct);
                ingested++;
            }
            catch (Exception ex)
            {
                errors.Add($"File {file}: {ex.Message}");
                _logger.LogWarning(ex, "Failed to ingest auto-memory file: {File}", file);
            }
        }
        return ingested;
    }

    private async Task<int> PruneStaleMemoriesAsync(CancellationToken ct)
    {
        var retentionDays = _settings.AutoMemory.ResolvedShortTermRetentionDays;
        if (retentionDays == 0) return 0;
        if (retentionDays < 0)
        {
            _logger.LogWarning("AutoMemory.ShortTermRetentionDays is negative ({Days}); pruning disabled.", retentionDays);
            return 0;
        }
        var cutoff = DateTimeOffset.UtcNow.AddDays(-retentionDays);
        return await _store.DeleteMemoriesByTierOlderThanAsync(MemoryTier.ShortTerm, cutoff, ct);
    }

    private async Task<int> ReindexDirtySourcesAsync(List<string> errors, CancellationToken ct)
    {
        var sources = await _store.ListSourcesAsync(1000, ct);
        int reindexed = 0;
        foreach (var source in sources)
        {
            if (source.SourcePath.StartsWith("http://", StringComparison.OrdinalIgnoreCase) ||
                source.SourcePath.StartsWith("https://", StringComparison.OrdinalIgnoreCase))
                continue;

            if (!File.Exists(source.SourcePath))
            {
                errors.Add($"Source file missing (not deleted): {source.SourcePath}");
                _logger.LogWarning("RAG source file missing on disk: {Path}", source.SourcePath);
                continue;
            }

            var content = await File.ReadAllTextAsync(source.SourcePath, ct);
            var currentHash = ComputeHash(content);
            if (currentHash == source.ContentHash) continue;

            await _indexer.ReindexSourceAsync(source.SourcePath, ct);
            reindexed++;
        }
        return reindexed;
    }

    /// <summary>Computes a SHA-256 hex hash of the given string content.</summary>
    public static string ComputeHash(string content)
    {
        var bytes = SHA256.HashData(Encoding.UTF8.GetBytes(content));
        return Convert.ToHexString(bytes).ToLowerInvariant();
    }

    private static string ResolveBaseDirectory(string path)
    {
        if (path.StartsWith("~/") || path == "~")
        {
            var home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            path = home + path[1..];
        }
        return path;
    }
}
