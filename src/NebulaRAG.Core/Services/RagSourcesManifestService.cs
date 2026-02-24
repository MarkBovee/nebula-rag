using System.Text;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

/// <summary>
/// Synchronizes a markdown manifest that describes currently indexed RAG sources and ingestion settings.
/// </summary>
public sealed class RagSourcesManifestService
{
    private readonly PostgresRagStore _store;
    private readonly RagSettings _settings;
    private readonly ILogger<RagSourcesManifestService> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="RagSourcesManifestService"/> class.
    /// </summary>
    /// <param name="store">Store used to retrieve indexed source metadata.</param>
    /// <param name="settings">Runtime settings used to emit chunking and embedding metadata.</param>
    /// <param name="logger">Logger for non-fatal synchronization diagnostics.</param>
    public RagSourcesManifestService(PostgresRagStore store, RagSettings settings, ILogger<RagSourcesManifestService> logger)
    {
        _store = store;
        _settings = settings;
        _logger = logger;
    }

    /// <summary>
    /// Synchronizes the rag-sources markdown document after an index mutation operation.
    /// </summary>
    /// <param name="contextPath">Optional source path used to resolve where the manifest should be written.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Information about the synchronized manifest output.</returns>
    public async Task<RagSourcesManifestSyncResult> SyncAsync(string? contextPath, CancellationToken cancellationToken = default)
    {
        var manifestPath = ResolveManifestPath(contextPath);
        var sources = await _store.ListSourcesAsync(cancellationToken: cancellationToken);
        var markdown = BuildMarkdown(sources);

        var directory = Path.GetDirectoryName(manifestPath);
        if (!string.IsNullOrWhiteSpace(directory) && !Directory.Exists(directory))
        {
            Directory.CreateDirectory(directory);
        }

        await File.WriteAllTextAsync(manifestPath, markdown, Encoding.UTF8, cancellationToken);
        _logger.LogInformation("RAG sources manifest synchronized at {ManifestPath} with {SourceCount} sources.", manifestPath, sources.Count);

        return new RagSourcesManifestSyncResult(manifestPath, sources.Count);
    }

    /// <summary>
    /// Resolves the destination path for rag-sources markdown output.
    /// </summary>
    /// <param name="contextPath">Optional source path that triggered synchronization.</param>
    /// <returns>Absolute path where the manifest should be written.</returns>
    private static string ResolveManifestPath(string? contextPath)
    {
        var manifestFileName = "rag-sources.md";

        if (!string.IsNullOrWhiteSpace(contextPath))
        {
            try
            {
                if (Path.IsPathRooted(contextPath))
                {
                    if (Directory.Exists(contextPath))
                    {
                        return Path.Combine(contextPath, manifestFileName);
                    }

                    var parentDirectory = Path.GetDirectoryName(contextPath);
                    if (!string.IsNullOrWhiteSpace(parentDirectory))
                    {
                        return Path.Combine(parentDirectory, manifestFileName);
                    }
                }
            }
            catch
            {
                // Fallback to current directory when context path cannot be resolved safely.
            }
        }

        return Path.Combine(Directory.GetCurrentDirectory(), manifestFileName);
    }

    /// <summary>
    /// Builds markdown content for the rag-sources manifest from currently indexed sources.
    /// </summary>
    /// <param name="sources">Indexed sources from storage.</param>
    /// <returns>Markdown document content.</returns>
    private string BuildMarkdown(IReadOnlyList<SourceInfo> sources)
    {
        var includePattern = string.Join(", ", _settings.Ingestion.IncludeExtensions.Select(extension => $"*{extension}"));
        var excludePattern = string.Join(", ", _settings.Ingestion.ExcludeDirectories.Select(directory => $"**/{directory}/**"));
        var embeddingModel = string.IsNullOrWhiteSpace(_settings.Ingestion.EmbeddingModel)
            ? "unknown"
            : _settings.Ingestion.EmbeddingModel;

        var builder = new StringBuilder();
        builder.AppendLine("# RAG Sources");
        builder.AppendLine();
        builder.AppendLine("This file is automatically synchronized by NebulaRAG after index mutations.");
        builder.AppendLine();
        builder.AppendLine("## Source Inventory");
        builder.AppendLine();
        builder.AppendLine("| Source Path | Include Pattern | Exclude Pattern | Chunk Size | Chunk Overlap | Embedding Model | Last Indexed (UTC) |");
        builder.AppendLine("|---|---|---|---|---|---|---|");

        foreach (var source in sources)
        {
            var indexedAtUtc = source.IndexedAt.ToUniversalTime().ToString("yyyy-MM-ddTHH:mm:ssZ");
            builder.AppendLine($"| `{source.SourcePath}` | `{includePattern}` | `{excludePattern}` | {_settings.Ingestion.ChunkSize} | {_settings.Ingestion.ChunkOverlap} | `{embeddingModel}` | `{indexedAtUtc}` |");
        }

        if (sources.Count == 0)
        {
            builder.AppendLine("| _none_ | - | - | - | - | - | - |");
        }

        builder.AppendLine();
        builder.AppendLine("## Chunking Defaults");
        builder.AppendLine();
        builder.AppendLine($"- Default chunk size: `{_settings.Ingestion.ChunkSize}`");
        builder.AppendLine($"- Default overlap: `{_settings.Ingestion.ChunkOverlap}`");
        builder.AppendLine($"- Vector dimensions: `{_settings.Ingestion.VectorDimensions}`");

        return builder.ToString();
    }
}

/// <summary>
/// Result model describing a synchronized rag-sources manifest output.
/// </summary>
/// <param name="ManifestPath">Absolute path of the synchronized manifest file.</param>
/// <param name="SourceCount">Number of indexed source rows included in the file.</param>
public sealed record RagSourcesManifestSyncResult(string ManifestPath, int SourceCount);
