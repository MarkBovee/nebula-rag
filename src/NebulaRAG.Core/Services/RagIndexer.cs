using System.Security.Cryptography;
using System.Text;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

public sealed class RagIndexer
{
    private readonly PostgresRagStore _store;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RagSettings _settings;

    public RagIndexer(
        PostgresRagStore store,
        TextChunker chunker,
        IEmbeddingGenerator embeddingGenerator,
        RagSettings settings)
    {
        _store = store;
        _chunker = chunker;
        _embeddingGenerator = embeddingGenerator;
        _settings = settings;
    }

    public async Task<IndexSummary> IndexDirectoryAsync(string sourceDirectory, CancellationToken cancellationToken = default)
    {
        if (!Directory.Exists(sourceDirectory))
        {
            throw new DirectoryNotFoundException($"Source directory does not exist: {sourceDirectory}");
        }

        var summary = new IndexSummary();
        var extensions = new HashSet<string>(_settings.Ingestion.IncludeExtensions, StringComparer.OrdinalIgnoreCase);
        var files = Directory.EnumerateFiles(sourceDirectory, "*", SearchOption.AllDirectories);

        foreach (var filePath in files)
        {
            if (!extensions.Contains(Path.GetExtension(filePath)))
            {
                continue;
            }

            var info = new FileInfo(filePath);
            if (info.Length > _settings.Ingestion.MaxFileSizeBytes)
            {
                summary.DocumentsSkipped++;
                continue;
            }

            var content = await File.ReadAllTextAsync(filePath, cancellationToken);
            if (string.IsNullOrWhiteSpace(content))
            {
                summary.DocumentsSkipped++;
                continue;
            }

            var chunks = _chunker.Chunk(content, _settings.Ingestion.ChunkSize, _settings.Ingestion.ChunkOverlap);
            if (chunks.Count == 0)
            {
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
                summary.DocumentsSkipped++;
                continue;
            }

            summary.DocumentsIndexed++;
            summary.ChunksIndexed += chunkEmbeddings.Count;
        }

        return summary;
    }
}

public sealed class IndexSummary
{
    public int DocumentsIndexed { get; set; }
    public int DocumentsSkipped { get; set; }
    public int ChunksIndexed { get; set; }
}
