using System.Security.Cryptography;
using System.Text;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Exceptions;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Pathing;
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
    /// <param name="projectName">Optional explicit project-name prefix for stored source keys.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Summary of indexing results.</returns>
    /// <exception cref="RagIndexingException">Thrown if indexing fails.</exception>
    public async Task<IndexSummary> IndexDirectoryAsync(string sourceDirectory, string? projectName = null, CancellationToken cancellationToken = default)
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
            var sourceRootPath = Path.GetFullPath(sourceDirectory);
            var projectRootPath = Path.GetFullPath(Directory.GetCurrentDirectory());
            var extensions = new HashSet<string>(_settings.Ingestion.IncludeExtensions, StringComparer.OrdinalIgnoreCase);
            var excludedDirectories = new HashSet<string>(_settings.Ingestion.ExcludeDirectories, StringComparer.OrdinalIgnoreCase);
            var excludedFileNames = new HashSet<string>(_settings.Ingestion.ExcludeFileNames, StringComparer.OrdinalIgnoreCase);
            var excludedFileSuffixes = _settings.Ingestion.ExcludeFileSuffixes;
            var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories).ToList();

            _logger.LogInformation("Found {FileCount} files in directory", files.Count);

            foreach (var filePath in files)
            {
                if (IsInExcludedDirectory(filePath, excludedDirectories))
                {
                    summary.DocumentsSkipped++;
                    continue;
                }

                if (ShouldSkipGeneratedFile(filePath, excludedFileNames, excludedFileSuffixes))
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
                    var sourcePath = BuildSourceStoragePath(projectRootPath, sourceRootPath, filePath, projectName);
                    var wasUpdated = await _store.UpsertDocumentAsync(sourcePath, hash, chunkEmbeddings, cancellationToken);

                    if (!wasUpdated)
                    {
                        _logger.LogDebug("Skipping file (unchanged): {File}", filePath);
                        summary.DocumentsSkipped++;
                        continue;
                    }

                    _logger.LogInformation("Indexed document: {File} ({ChunkCount} chunks)", sourcePath, chunkEmbeddings.Count);
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

    /// <summary>
    /// Returns true when file looks like generated output (bundles, source maps, lockfiles, etc.).
    /// </summary>
    private static bool ShouldSkipGeneratedFile(string filePath, HashSet<string> excludedFileNames, IReadOnlyCollection<string> excludedFileSuffixes)
    {
        var fileName = Path.GetFileName(filePath);
        if (excludedFileNames.Contains(fileName))
        {
            return true;
        }

        var normalizedFileName = fileName.ToLowerInvariant();
        if (excludedFileSuffixes.Any(suffix => normalizedFileName.EndsWith(suffix, StringComparison.OrdinalIgnoreCase)))
        {
            return true;
        }

        // Common generated/bundled naming patterns from frontend build tools.
        return normalizedFileName.Contains(".generated.", StringComparison.Ordinal)
               || normalizedFileName.Contains(".g.", StringComparison.Ordinal)
               || normalizedFileName.Contains("bundle", StringComparison.Ordinal)
               || normalizedFileName.Contains("chunk", StringComparison.Ordinal)
               || HasHashedBundleMarker(normalizedFileName);
    }

    /// <summary>
    /// Converts a file path to a project-relative source key with forward slashes.
    /// </summary>
    private static string BuildSourceStoragePath(string projectRootPath, string sourceRootPath, string filePath, string? projectName)
    {
        var normalizedSourcePath = SourcePathNormalizer.IsPathUnderRoot(filePath, projectRootPath)
            ? SourcePathNormalizer.NormalizeForStorage(filePath, projectRootPath)
            : SourcePathNormalizer.NormalizeForStorage(filePath, sourceRootPath);

        return SourcePathNormalizer.ApplyExplicitProjectPrefix(normalizedSourcePath, projectName);
    }

    /// <summary>
    /// Detects hashed bundle file names like app.a1b2c3d4.js.
    /// </summary>
    private static bool HasHashedBundleMarker(string fileName)
    {
        var extensionIndex = fileName.LastIndexOf('.');
        if (extensionIndex <= 0)
        {
            return false;
        }

        var stem = fileName[..extensionIndex];
        var lastDot = stem.LastIndexOf('.');
        if (lastDot <= 0)
        {
            return false;
        }

        var suffix = stem[(lastDot + 1)..];
        return suffix.Length >= 6 && suffix.All(char.IsLetterOrDigit);
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
