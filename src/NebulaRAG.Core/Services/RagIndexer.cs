using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Indexes documents and creates embedding vectors.
/// </summary>
public sealed class RagIndexer
{
    private readonly PostgresRagStore _store;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RagSettings _settings;
    private readonly ILogger<RagIndexer> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagIndexer"/> class.
    /// </summary>
    public RagIndexer(
        PostgresRagStore store,
        TextChunker chunker,
        IEmbeddingGenerator embeddingGenerator,
        RagSettings settings,
        ILogger<RagIndexer> logger)
    {
        _store = store ?? throw new ArgumentNullException(nameof(store));
        _chunker = chunker ?? throw new ArgumentNullException(nameof(chunker));
        _embeddingGenerator = embeddingGenerator ?? throw new ArgumentNullException(nameof(embeddingGenerator));
        _settings = settings ?? throw new ArgumentNullException(nameof(settings));
        _logger = logger ?? throw new ArgumentNullException(nameof(logger));
    }

    /// <summary>
    /// Indexes all documents in a directory recursively.
    /// </summary>
    /// <param name="sourceDirectory">The root directory to index.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of indexing results.</returns>
    /// <exception cref="RagIndexingException">Thrown if indexing fails.</exception>
    public async Task<IndexSummary> IndexDirectoryAsync(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            _logger.LogError("Source directory does not exist: {Directory}", sourceDirectory);
            throw new RagIndexingException($"Source directory does not exist: {sourceDirectory}");
        }

        _logger.LogInformation("Starting index of directory: {Directory}", sourceDirectory);

        try
        {
            var summary = new IndexSummary();
            var extensions = new HashSet<string>(_settings.Ingestion.IncludeExtensions, StringComparer.OrdinalIgnoreCase);
            var excludedDirectories = new HashSet<string>(_settings.Ingestion.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);
            var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).ToList();

            _logger.LogInformation("Found {FileCount} files in directory", files.Count);

            foreach (var filePath in files)
            {
                if (IsInExcludedDirectory(filePath, excludedDirectories))
                {
                    summary.DocumentsSkipped++;
                    continue;
                }

                if (!extensions.Contains(Path.GetExtension(filePath)))
                {
                    continue;
                }

                try
                {
                    var info = new FileInfo(filePath);
                    if (info.Length > _settings.Ingestion.MaxFileSizeBytes)
                    {
                        _logger.LogDebug("Skipping file (too large): {File} ({Size} bytes)", filePath, info.Length);
                        summary.DocumentsSkipped++;
                        continue;
                    }

                    var content = await File.ReadAllTextAsync(filePath, cancellationToken);
                    if (string.IsNullOrWhiteSpace(content))
                    {
                        _logger.LogDebug("Skipping file (empty): {File}", filePath);
                        summary.DocumentsSkipped++;
                        continue;
                    }

                    var chunks = _chunker.Chunk(content, _settings.Ingestion.ChunkSize, _settings.Ingestion.ChunkOverlap);
                    if (chunks.Count == 0)
                    {
                        _logger.LogDebug("Skipping file (no chunks): {File}", filePath);
                        summary.DocumentsSkipped++;
                        continue;
                    }

                    var chunkEmbeddings = chunks
                        .Select(chunk => new ChunkEmbedding(
                            chunk.Index,
                            chunk.Text,
                            chunk.TokenCount,
                            _embeddingGenerator.GenerateEmbedding(chunk.Text, _settings.Ingestion.VectorDimensions)))
                        .ToList();

                    var hash = Convert.ToHexString(SHA256.HashData(Encoding.UTF8.GetBytes(content)));
                    var wasUpdated = await _store.UpsertDocumentAsync(filePath, hash, chunkEmbeddings, cancellationToken);

                    if (!wasUpdated)
                    {
                        _logger.LogDebug("Skipping file (unchanged): {File}", filePath);
                        summary.DocumentsSkipped++;
                        continue;
                    }

                    _logger.LogInformation("Indexed document: {File} ({ChunkCount} chunks)", filePath, chunkEmbeddings.Count);
                    summary.DocumentsIndexed++;
                    summary.ChunksIndexed += chunkEmbeddings.Count;
                }
                catch (Exception ex) when (!(ex is RagException))
                {
                    _logger.LogError(ex, "Error indexing file: {File}", filePath);
                    // Continue with next file instead of failing entire index
                    summary.DocumentsSkipped++;
                }
            }

            _logger.LogInformation(
                "Index complete: {DocsIndexed} documents indexed, {ChunksIndexed} chunks, {DocsSkipped} skipped",
                summary.DocumentsIndexed,
                summary.ChunksIndexed,
                summary.DocumentsSkipped);

            return summary;
        }
        catch (Exception ex) when (!(ex is RagException))
        {
            _logger.LogError(ex, "Indexing failed for directory: {Directory}", sourceDirectory);
            throw new RagIndexingException($"Failed to index directory: {sourceDirectory}", ex);
        }
    }

    /// <summary>
    /// Returns true when a file path contains a configured excluded directory segment.
    /// </summary>
    private static bool IsInExcludedDirectory(string filePath, HashSet<string> excludedDirectories)
    {
        var pathSegments = filePath.Split(Path.DirectorySeparatorChar, Path.AltDirectorySeparatorChar);
        return pathSegments.Any(excludedDirectories.Contains);
    }
}

/// <summary>
/// Summary of an indexing operation.
/// </summary>
public sealed class IndexSummary
{
    /// <summary>
    /// Number of documents successfully indexed.
    /// </summary>
    public int DocumentsIndexed { get; set; }

    /// <summary>
    /// Number of documents skipped.
    /// </summary>
    public int DocumentsSkipped { get; set; }

    /// <summary>
    /// Total number of chunks indexed.
    /// </summary>
    public int ChunksIndexed { get; set; }
}
