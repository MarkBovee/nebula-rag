using System.Globalization;
using System.Text;
using NebulaRAG.Core.Models;
using Npgsql;

namespace NebulaRAG.Core.Storage;

/// <summary>
/// PostgreSQL-based storage backend for the RAG system.
/// Manages document index, chunks, embeddings, and semantic search via pgvector.
/// </summary>
public sealed class PostgresRagStore
{
    private readonly string _connectionString;

    /// <summary>
    /// Initializes a new instance of the <see cref="PostgresRagStore"/> class.
    /// </summary>
    /// <param name="connectionString">The PostgreSQL connection string.</param>
    /// <exception cref="ArgumentException">Thrown if connectionString is null or empty.</exception>
    public PostgresRagStore(string connectionString)
    {
        if (string.IsNullOrWhiteSpace(connectionString))
        {
            throw new ArgumentException("Connection string cannot be null or empty.", nameof(connectionString));
        }

        _connectionString = connectionString;
    }

    /// <summary>
    /// Initializes the PostgreSQL schema including tables, indexes, and pgvector extension.
    /// Creates rag_documents, rag_chunks tables, and sets up vector similarity search indexes.
    /// </summary>
    /// <param name="vectorDimensions">The dimension count for vector embeddings.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="ArgumentOutOfRangeException">Thrown if vectorDimensions is not greater than 0.</exception>
    public async Task InitializeSchemaAsync(int vectorDimensions, CancellationToken cancellationToken = default)
    {
        if (vectorDimensions <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(vectorDimensions), "Vector dimensions must be greater than 0.");
        }

        var sql = $"""
            CREATE EXTENSION IF NOT EXISTS vector;

            CREATE TABLE IF NOT EXISTS rag_documents (
                id BIGSERIAL PRIMARY KEY,
                source_path TEXT NOT NULL UNIQUE,
                content_hash TEXT NOT NULL,
                indexed_at TIMESTAMPTZ NOT NULL DEFAULT NOW()
            );

            CREATE TABLE IF NOT EXISTS rag_chunks (
                id BIGSERIAL PRIMARY KEY,
                document_id BIGINT NOT NULL REFERENCES rag_documents(id) ON DELETE CASCADE,
                chunk_index INT NOT NULL,
                chunk_text TEXT NOT NULL,
                token_count INT NOT NULL,
                embedding VECTOR({vectorDimensions}) NOT NULL,
                UNIQUE(document_id, chunk_index)
            );

            CREATE INDEX IF NOT EXISTS ix_rag_chunks_document_id ON rag_chunks(document_id);
            CREATE INDEX IF NOT EXISTS ix_rag_chunks_embedding_ivfflat
                ON rag_chunks
                USING ivfflat (embedding vector_cosine_ops)
                WITH (lists = 100);
            CREATE INDEX IF NOT EXISTS ix_rag_chunks_text_search
                ON rag_chunks
                USING gin (to_tsvector('english', chunk_text));
            """;

        // Open connection and execute schema creation, including pgvector extension
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Inserts or updates a document and its chunks in the index.
    /// If the source path exists with the same content hash, returns false (no update performed).
    /// Otherwise, deletes old chunks and inserts new ones in a single transaction.
    /// </summary>
    /// <param name="sourcePath\">The unique source path or identifier for this document.</param>\n    /// <param name=\"contentHash\">SHA256 hash of the document content for change detection.</param>
    /// <param name=\"chunks\">Collection of chunk embeddings to store for this document.</param>
    /// <param name=\"cancellationToken\">Cancellation token.</param>
    /// <returns>True if the document was inserted or updated; false if unchanged.</returns>
    /// <exception cref=\"ArgumentException\">Thrown if sourcePath or contentHash is null or empty.</exception>
    public async Task<bool> UpsertDocumentAsync(
        string sourcePath,
        string contentHash,
        IReadOnlyList<ChunkEmbedding> chunks,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException(\"Source path cannot be null or empty.\", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(contentHash))
        {
            throw new ArgumentException(\"Content hash cannot be null or empty.\", nameof(contentHash));
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Check if document already exists with the same content hash
        long? existingDocumentId = null;
        string? existingHash = null;

        await using (var findCommand = new NpgsqlCommand(
                         \"SELECT id, content_hash FROM rag_documents WHERE source_path = @sourcePath\",
                         connection,
                         transaction))
        {
            findCommand.Parameters.AddWithValue(\"sourcePath\", sourcePath);

            await using var reader = await findCommand.ExecuteReaderAsync(cancellationToken);
            if (await reader.ReadAsync(cancellationToken))
            {
                existingDocumentId = reader.GetInt64(0);
                existingHash = reader.GetString(1);
            }
        }

        if (existingDocumentId.HasValue && string.Equals(existingHash, contentHash, StringComparison.Ordinal))
        {
            // Document already exists with same content hash, no update needed
            await transaction.RollbackAsync(cancellationToken);
            return false;
        }

        // Update existing document or insert new one
        long documentId;
        if (existingDocumentId.HasValue)
        {
            await using (var updateCommand = new NpgsqlCommand(
                             """
                             UPDATE rag_documents
                             SET content_hash = @contentHash, indexed_at = NOW()
                             WHERE id = @documentId
                             """,
                             connection,
                             transaction))
            {
                updateCommand.Parameters.AddWithValue("contentHash", contentHash);
                updateCommand.Parameters.AddWithValue("documentId", existingDocumentId.Value);
                await updateCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            await using (var deleteChunksCommand = new NpgsqlCommand(
                             "DELETE FROM rag_chunks WHERE document_id = @documentId",
                             connection,
                             transaction))
            {
                deleteChunksCommand.Parameters.AddWithValue("documentId", existingDocumentId.Value);
                await deleteChunksCommand.ExecuteNonQueryAsync(cancellationToken);
            }

            documentId = existingDocumentId.Value;
        }
        else
        {
            await using var insertDocumentCommand = new NpgsqlCommand(
                """
                INSERT INTO rag_documents (source_path, content_hash)
                VALUES (@sourcePath, @contentHash)
                RETURNING id
                """,
                connection,
                transaction);

            insertDocumentCommand.Parameters.AddWithValue("sourcePath", sourcePath);
            insertDocumentCommand.Parameters.AddWithValue("contentHash", contentHash);
            documentId = (long)(await insertDocumentCommand.ExecuteScalarAsync(cancellationToken)
                                ?? throw new InvalidOperationException("Failed to insert document."));
        }

        const string insertChunkSql = """
            INSERT INTO rag_chunks (document_id, chunk_index, chunk_text, token_count, embedding)
            VALUES (@documentId, @chunkIndex, @chunkText, @tokenCount, CAST(@embedding AS vector))
            """;

        // Insert all chunks for this document
        foreach (var chunk in chunks)
        {
            await using var insertChunkCommand = new NpgsqlCommand(insertChunkSql, connection, transaction);
            insertChunkCommand.Parameters.AddWithValue("documentId", documentId);
            insertChunkCommand.Parameters.AddWithValue("chunkIndex", chunk.ChunkIndex);
            insertChunkCommand.Parameters.AddWithValue("chunkText", chunk.ChunkText);
            insertChunkCommand.Parameters.AddWithValue("tokenCount", chunk.TokenCount);
            insertChunkCommand.Parameters.AddWithValue("embedding", ToVectorLiteral(chunk.Embedding));
            await insertChunkCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return true;
    }

    /// <summary>
    /// Executes a semantic search using vector similarity.
    /// Returns the top K most similar chunks ranked by cosine distance.
    /// </summary>
    /// <param name=\"queryEmbedding\">The query vector embedding.</param>
    /// <param name=\"topK\">Maximum number of results to return (must be > 0).</param>
    /// <param name=\"cancellationToken\">Cancellation token.</param>
    /// <returns>List of search results ranked by relevance (descending score).</returns>
    /// <exception cref=\"ArgumentException\">Thrown if queryEmbedding is empty.</exception>
    /// <exception cref=\"ArgumentOutOfRangeException\">Thrown if topK is not greater than 0.</exception>
    public async Task<IReadOnlyList<RagSearchResult>> SearchAsync(
        float[] queryEmbedding,
        int topK,
        CancellationToken cancellationToken = default)
    {
        if (queryEmbedding.Length == 0)
        {
            throw new ArgumentException("Query embedding cannot be empty.", nameof(queryEmbedding));
        }

        if (topK <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topK), "TopK must be greater than 0.");
        }

        const string sql = """
            SELECT d.source_path,
                   c.chunk_index,
                   c.chunk_text,
                   1 - (c.embedding <=> CAST(@embedding AS vector)) AS score
            FROM rag_chunks c
            INNER JOIN rag_documents d ON c.document_id = d.id
            ORDER BY c.embedding <=> CAST(@embedding AS vector)
            LIMIT @topK
            """;

        var results = new List<RagSearchResult>();
        // Use cosine distance operator (<=>)to find nearest neighbors
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(queryEmbedding));
        command.Parameters.AddWithValue("topK", topK);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            var scoreValue = reader.GetValue(3);
            // Convert distance to similarity (1 - distance for cosine)
            var score = scoreValue is double d
                ? d
                : Convert.ToDouble(scoreValue, CultureInfo.InvariantCulture);

            results.Add(new RagSearchResult(
                reader.GetString(0),
                reader.GetInt32(1),
                reader.GetString(2),
                score));
        }

        return results;
    }

    /// <summary>
    /// Retrieves the most recently indexed documents.
    /// </summary>
    /// <param name=\"limit\">Maximum number of documents to return.</param>
    /// <param name=\"cancellationToken\">Cancellation token.</param>
    /// <returns>List of recent documents ordered by indexed_at descending.</returns>
    /// <exception cref=\"ArgumentOutOfRangeException\">Thrown if limit is not greater than 0.</exception>
    public async Task<IReadOnlyList<RagIndexedDocumentSummary>> GetRecentDocumentsAsync(
        int limit,
        CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0.");
        }

        const string sql = """
            SELECT d.source_path,
                   d.indexed_at,
                   COUNT(c.id)::INT AS chunk_count
            FROM rag_documents d
            LEFT JOIN rag_chunks c ON c.document_id = d.id
            GROUP BY d.id, d.source_path, d.indexed_at
            ORDER BY d.indexed_at DESC
            LIMIT @limit
            """;

        var documents = new List<RagIndexedDocumentSummary>();
        // Group by document and aggregate chunk counts
        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            documents.Add(new RagIndexedDocumentSummary(
                reader.GetString(0),
                reader.GetFieldValue<DateTimeOffset>(1),
                reader.GetInt32(2)));
        }

        return documents;
    }

    /// <summary>
    /// Gets detailed index statistics including document count, chunk count, token estimates, and date range.
    /// </summary>
    /// <param name=\"cancellationToken\">Cancellation token.</param>
    /// <returns>Aggregated index statistics.</returns>
    public async Task<IndexStats> GetIndexStatsAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT 
                COUNT(DISTINCT d.id) as doc_count,
                COUNT(c.id) as chunk_count,
                COALESCE(SUM(c.token_count), 0) as total_tokens,
                MIN(d.indexed_at) as oldest,
                MAX(d.indexed_at) as newest
            FROM rag_documents d
            LEFT JOIN rag_chunks c ON d.id = c.document_id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new IndexStats(
                DocumentCount: reader.GetInt32(0),
                ChunkCount: reader.GetInt32(1),
                TotalTokens: reader.GetInt64(2),
                OldestIndexedAt: reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                NewestIndexedAt: reader.IsDBNull(4) ? null : reader.GetDateTime(4));
        }

        return new IndexStats(0, 0, 0, null, null);
    }

    /// <summary>
    /// Gets all indexed document sources with their chunk counts.
    /// </summary>
    public async Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            SELECT d.source_path, COUNT(c.id) as chunk_count, d.indexed_at, d.content_hash
            FROM rag_documents d
            LEFT JOIN rag_chunks c ON d.id = c.document_id
            GROUP BY d.id, d.source_path, d.indexed_at, d.content_hash
            ORDER BY d.indexed_at DESC";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sources = new List<SourceInfo>();
        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);

        while (await reader.ReadAsync(cancellationToken))
        {
            sources.Add(new SourceInfo(
                SourcePath: reader.GetString(0),
                ChunkCount: reader.GetInt32(1),
                IndexedAt: reader.GetDateTime(2),
                ContentHash: reader.GetString(3)));
        }

        return sources.AsReadOnly();
    }

    /// <summary>
    /// Deletes all chunks for a specific document source.
    /// </summary>
    /// <param name="sourcePath">The source path to delete.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Number of documents deleted.</returns>
    public async Task<int> DeleteSourceAsync(string sourcePath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
        }

        const string sql = @"
            DELETE FROM rag_documents
            WHERE source_path = @sourcePath";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("sourcePath", sourcePath);

        return await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Permanently deletes all indexed documents and chunks from the RAG store.
    /// WARNING: This operation cannot be undone.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    public async Task PurgeAllAsync(CancellationToken cancellationToken = default)
    {
        const string sql = @"
            DELETE FROM rag_chunks;
            DELETE FROM rag_documents;";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        // Cascading delete ensures chunks are removed when documents are cleared
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteNonQueryAsync(cancellationToken);
    }

    /// <summary>
    /// Performs a health check by testing database connectivity and basic functionality.
    /// </summary>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <exception cref="Exception">Thrown if the database connection or query fails.</exception>
    public async Task HealthCheckAsync(CancellationToken cancellationToken = default)
    {
        const string sql = "SELECT 1";

        await using var connection = new NpgsqlConnection(_connectionString);
        // Attempt to open connection and execute simple query
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        await command.ExecuteScalarAsync(cancellationToken);
    }

    /// <summary>
    /// Converts a float array to a PostgreSQL vector literal string format.
    /// </summary>
    /// <remarks>
    /// Produces output like "[0.5, 0.3, -0.2]" suitable for pgvector operations.
    /// </remarks>
    private static string ToVectorLiteral(IReadOnlyList<float> embedding)
    {
        var builder = new StringBuilder("[");
        for (var i = 0; i < embedding.Count; i++)
        {
            if (i > 0)
            {
                builder.Append(',');
            }

            // Format with sufficient precision and invariant culture for PostgreSQL compatibility
            builder.Append(embedding[i].ToString("0.######", CultureInfo.InvariantCulture));
        }

        builder.Append(']');
        return builder.ToString();
    }
}
