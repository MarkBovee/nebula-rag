using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Services;

public sealed class RagQueryService
{
    private readonly PostgresRagStore _store;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RagSettings _settings;

    public RagQueryService(PostgresRagStore store, IEmbeddingGenerator embeddingGenerator, RagSettings settings)
    {
        _store = store;
        _embeddingGenerator = embeddingGenerator;
        _settings = settings;
    }

    public Task<IReadOnlyList<RagSearchResult>> QueryAsync(
        string queryText,
        int? topK = null,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(queryText))
        {
            throw new ArgumentException("Query text cannot be null or empty.", nameof(queryText));
        }

        var embedding = _embeddingGenerator.GenerateEmbedding(queryText, _settings.Ingestion.VectorDimensions);
        var resultCount = topK ?? _settings.Retrieval.DefaultTopK;
        return _store.SearchAsync(embedding, resultCount, cancellationToken);
    }
}
