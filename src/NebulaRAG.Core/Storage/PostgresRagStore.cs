using System.Globalization;
using System.Text;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Pathing;
using Npgsql;
using NpgsqlTypes;

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

            CREATE TABLE IF NOT EXISTS memories (
                id BIGSERIAL PRIMARY KEY,
                session_id TEXT NOT NULL,
                type TEXT NOT NULL CHECK (type IN ('episodic', 'semantic', 'procedural')),
                content TEXT NOT NULL,
                embedding VECTOR({vectorDimensions}) NOT NULL,
                created_at TIMESTAMPTZ NOT NULL DEFAULT NOW(),
                tags TEXT[] NOT NULL DEFAULT ARRAY[]::TEXT[]
            );

            CREATE INDEX IF NOT EXISTS ix_rag_chunks_document_id ON rag_chunks(document_id);
            CREATE INDEX IF NOT EXISTS ix_rag_chunks_embedding_ivfflat
                ON rag_chunks
                USING ivfflat (embedding vector_cosine_ops)
                WITH (lists = 100);
            CREATE INDEX IF NOT EXISTS ix_rag_chunks_text_search
                ON rag_chunks
                USING gin (to_tsvector('english', chunk_text));

            CREATE INDEX IF NOT EXISTS ix_memories_created_at ON memories (created_at DESC);
            CREATE INDEX IF NOT EXISTS ix_memories_type ON memories (type);
            CREATE INDEX IF NOT EXISTS ix_memories_tags_gin ON memories USING GIN (tags);
            CREATE INDEX IF NOT EXISTS ix_memories_embedding_ivfflat
                ON memories
                USING ivfflat (embedding vector_cosine_ops)
                WITH (lists = 100);
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
    /// <param name="sourcePath">The unique source path or identifier for this document.</param>
    /// <param name="contentHash">SHA256 hash of the document content for change detection.</param>
    /// <param name="chunks">Collection of chunk embeddings to store for this document.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>True if the document was inserted or updated; false if unchanged.</returns>
    /// <exception cref="ArgumentException">Thrown if sourcePath or contentHash is null or empty.</exception>
    public async Task<bool> UpsertDocumentAsync(
        string sourcePath,
        string contentHash,
        IReadOnlyList<ChunkEmbedding> chunks,
        CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            throw new ArgumentException("Source path cannot be null or empty.", nameof(sourcePath));
        }

        if (string.IsNullOrWhiteSpace(contentHash))
        {
            throw new ArgumentException("Content hash cannot be null or empty.", nameof(contentHash));
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        // Check if document already exists with the same content hash
        long? existingDocumentId = null;
        string? existingHash = null;

        await using (var findCommand = new NpgsqlCommand(
                         "SELECT id, content_hash FROM rag_documents WHERE source_path = @sourcePath",
                         connection,
                         transaction))
        {
            findCommand.Parameters.AddWithValue("sourcePath", sourcePath);

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
    /// <param name="cancellationToken">Cancellation token.</param>
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
        var scoreOrdinal = ResolveScoreOrdinal(reader);
        while (await reader.ReadAsync(cancellationToken))
        {
            // Convert distance to similarity (1 - distance for cosine).
            var score = TryReadScore(reader, scoreOrdinal, fallbackOrdinal: 3);

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
    /// <param name="includeIndexSize">When true, computes relation size bytes; otherwise returns 0 for size.</param>
    /// <param name=\"cancellationToken\">Cancellation token.</param>
    /// <returns>Aggregated index statistics.</returns>
    public async Task<IndexStats> GetIndexStatsAsync(bool includeIndexSize = false, CancellationToken cancellationToken = default)
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

        const string sizeSql = @"
            SELECT COALESCE(
                (SELECT pg_total_relation_size('rag_documents') + pg_total_relation_size('rag_chunks')),
                0
            )";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);

        var sourcePaths = new List<string>();
        await using (var projectCommand = new NpgsqlCommand("SELECT source_path FROM rag_documents", connection))
        await using (var projectReader = await projectCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await projectReader.ReadAsync(cancellationToken))
            {
                sourcePaths.Add(projectReader.GetString(0));
            }
        }

        var projectCount = CountDistinctProjects(sourcePaths);

        long indexSizeBytes = 0;
        if (includeIndexSize)
        {
            await using var sizeCommand = new NpgsqlCommand(sizeSql, connection);
            var sizeValue = await sizeCommand.ExecuteScalarAsync(cancellationToken);
            indexSizeBytes = sizeValue is null ? 0 : Convert.ToInt64(sizeValue, CultureInfo.InvariantCulture);
        }

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (await reader.ReadAsync(cancellationToken))
        {
            return new IndexStats(
                DocumentCount: reader.GetInt32(0),
                ChunkCount: reader.GetInt32(1),
                TotalTokens: reader.GetInt64(2),
                OldestIndexedAt: reader.IsDBNull(3) ? null : reader.GetDateTime(3),
                NewestIndexedAt: reader.IsDBNull(4) ? null : reader.GetDateTime(4),
                IndexSizeBytes: indexSizeBytes,
                ProjectCount: projectCount);
        }

        return new IndexStats(0, 0, 0, null, null, 0, 0);
    }

    /// <summary>
    /// Counts the number of distinct projects represented in indexed source paths.
    /// </summary>
    /// <param name="sourcePaths">Indexed source paths.</param>
    /// <returns>Distinct project count.</returns>
    private static int CountDistinctProjects(IEnumerable<string> sourcePaths)
    {
        var projectNames = new HashSet<string>(StringComparer.OrdinalIgnoreCase);

        foreach (var sourcePath in sourcePaths)
        {
            projectNames.Add(ExtractProjectName(sourcePath));
        }

        return projectNames.Count;
    }

    /// <summary>
    /// Extracts a normalized project key from a source path.
    /// </summary>
    /// <param name="sourcePath">Source path or URL.</param>
    /// <returns>Project key used for grouped statistics.</returns>
    private static string ExtractProjectName(string sourcePath)
    {
        if (string.IsNullOrWhiteSpace(sourcePath))
        {
            return "Unknown";
        }

        if (Uri.TryCreate(sourcePath, UriKind.Absolute, out var uri)
            && (uri.Scheme == Uri.UriSchemeHttp || uri.Scheme == Uri.UriSchemeHttps))
        {
            return uri.Host;
        }

        var normalizedPath = sourcePath.Replace('\\', '/');
        var pathSegments = normalizedPath.Split('/', StringSplitOptions.RemoveEmptyEntries);
        if (pathSegments.Length == 0)
        {
            return "Unknown";
        }

        if (pathSegments[0].Length == 2 && char.IsLetter(pathSegments[0][0]) && pathSegments[0][1] == ':')
        {
            return pathSegments.Length > 1 ? pathSegments[1] : pathSegments[0];
        }

        return pathSegments[0];
    }

    /// <summary>
    /// Gets all indexed document sources with their chunk counts.
    /// </summary>
    /// <param name="limit">Maximum number of sources to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent sources ordered by index time descending.</returns>
    public async Task<IReadOnlyList<SourceInfo>> ListSourcesAsync(int limit = 100, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0.");
        }

        const string sql = @"
            SELECT d.source_path, COUNT(c.id) as chunk_count, d.indexed_at, d.content_hash
            FROM rag_documents d
            LEFT JOIN rag_chunks c ON d.id = c.document_id
            GROUP BY d.id, d.source_path, d.indexed_at, d.content_hash
            ORDER BY d.indexed_at DESC
            LIMIT @limit";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var sources = new List<SourceInfo>();
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);
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
    /// Normalizes existing source keys to project-relative paths and resolves duplicates.
    /// </summary>
    /// <param name="projectRootPath">Project root used to convert absolute paths into relative source keys.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Tuple containing updated source key count and duplicate rows removed.</returns>
    public async Task<(int UpdatedCount, int DuplicatesRemoved)> NormalizeSourcePathsAsync(string projectRootPath, CancellationToken cancellationToken = default)
    {
        if (string.IsNullOrWhiteSpace(projectRootPath))
        {
            throw new ArgumentException("Project root path cannot be null or empty.", nameof(projectRootPath));
        }

        var sourceRows = new List<(long Id, string SourcePath, DateTime IndexedAt)>();

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        const string readSql = "SELECT id, source_path, indexed_at FROM rag_documents";
        await using (var readCommand = new NpgsqlCommand(readSql, connection))
        await using (var reader = await readCommand.ExecuteReaderAsync(cancellationToken))
        {
            while (await reader.ReadAsync(cancellationToken))
            {
                sourceRows.Add((reader.GetInt64(0), reader.GetString(1), reader.GetDateTime(2)));
            }
        }

        var normalizedRows = sourceRows
            .Select(row => new
            {
                row.Id,
                row.SourcePath,
                row.IndexedAt,
                NormalizedPath = SourcePathNormalizer.NormalizeForStorage(row.SourcePath, projectRootPath)
            })
            .ToList();

        var duplicateLoserIds = new List<long>();
        var renamePairs = new List<(long Id, string NewSourcePath)>();

        foreach (var group in normalizedRows.GroupBy(x => x.NormalizedPath, StringComparer.OrdinalIgnoreCase))
        {
            var ordered = group
                .OrderByDescending(x => x.IndexedAt)
                .ThenByDescending(x => x.Id)
                .ToList();

            var winner = ordered[0];
            if (!string.Equals(winner.SourcePath, winner.NormalizedPath, StringComparison.Ordinal))
            {
                renamePairs.Add((winner.Id, winner.NormalizedPath));
            }

            if (ordered.Count > 1)
            {
                duplicateLoserIds.AddRange(ordered.Skip(1).Select(x => x.Id));
            }
        }

        if (duplicateLoserIds.Count == 0 && renamePairs.Count == 0)
        {
            return (0, 0);
        }

        await using var transaction = await connection.BeginTransactionAsync(cancellationToken);

        var duplicatesRemoved = 0;
        foreach (var loserId in duplicateLoserIds)
        {
            await using var deleteCommand = new NpgsqlCommand("DELETE FROM rag_documents WHERE id = @id", connection, transaction);
            deleteCommand.Parameters.AddWithValue("id", loserId);
            duplicatesRemoved += await deleteCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        var updatedCount = 0;
        foreach (var renamePair in renamePairs)
        {
            await using var updateCommand = new NpgsqlCommand("UPDATE rag_documents SET source_path = @sourcePath WHERE id = @id", connection, transaction);
            updateCommand.Parameters.AddWithValue("sourcePath", renamePair.NewSourcePath);
            updateCommand.Parameters.AddWithValue("id", renamePair.Id);
            updatedCount += await updateCommand.ExecuteNonQueryAsync(cancellationToken);
        }

        await transaction.CommitAsync(cancellationToken);
        return (updatedCount, duplicatesRemoved);
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
    /// Retrieves a specific chunk by its primary key identifier.
    /// </summary>
    /// <param name="chunkId">Chunk row identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Chunk record when found; otherwise <c>null</c>.</returns>
    public async Task<ChunkRecord?> GetChunkByIdAsync(long chunkId, CancellationToken cancellationToken = default)
    {
        if (chunkId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(chunkId), "Chunk id must be greater than 0.");
        }

        const string sql = """
            SELECT c.id, d.source_path, c.chunk_index, c.chunk_text, c.token_count, d.indexed_at
            FROM rag_chunks c
            INNER JOIN rag_documents d ON d.id = c.document_id
            WHERE c.id = @chunkId
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("chunkId", chunkId);

        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        if (!await reader.ReadAsync(cancellationToken))
        {
            return null;
        }

        return new ChunkRecord(
            ChunkId: reader.GetInt64(0),
            SourcePath: reader.GetString(1),
            ChunkIndex: reader.GetInt32(2),
            ChunkText: reader.GetString(3),
            TokenCount: reader.GetInt32(4),
            IndexedAtUtc: reader.GetFieldValue<DateTimeOffset>(5));
    }

    /// <summary>
    /// Stores a memory entry and returns its newly assigned identifier.
    /// </summary>
    /// <param name="sessionId">Logical session identifier for grouping related memories.</param>
    /// <param name="type">Memory type: episodic, semantic, or procedural.</param>
    /// <param name="content">Natural language memory content.</param>
    /// <param name="tags">Tag collection for memory filtering.</param>
    /// <param name="embedding">Memory embedding vector.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Identifier of the inserted memory row.</returns>
    public async Task<long> CreateMemoryAsync(string sessionId, string type, string content, IReadOnlyList<string> tags, IReadOnlyList<float> embedding, CancellationToken cancellationToken = default)
    {
        ValidateMemoryArguments(sessionId, type, content, embedding);

        const string sql = """
            INSERT INTO memories (session_id, type, content, embedding, tags)
            VALUES (@sessionId, @type, @content, CAST(@embedding AS vector), @tags)
            RETURNING id
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("sessionId", sessionId);
        command.Parameters.AddWithValue("type", type);
        command.Parameters.AddWithValue("content", content);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(embedding));
        command.Parameters.AddWithValue("tags", tags.ToArray());

        var id = await command.ExecuteScalarAsync(cancellationToken);
        return (long)(id ?? throw new InvalidOperationException("Failed to insert memory."));
    }

    /// <summary>
    /// Lists the most recent memories optionally filtered by type and tag.
    /// </summary>
    /// <param name="limit">Maximum number of rows to return.</param>
    /// <param name="type">Optional memory type filter.</param>
    /// <param name="tag">Optional tag filter.</param>
    /// <param name="sessionId">Optional session-id filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent memory rows sorted by creation date descending.</returns>
    public async Task<IReadOnlyList<MemoryRecord>> ListMemoriesAsync(int limit, string? type = null, string? tag = null, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0.");
        }

        const string sql = """
            SELECT id, session_id, type, content, tags, created_at
            FROM memories
                        WHERE (@type::text IS NULL OR type = @type::text)
                            AND (@tag::text IS NULL OR @tag::text = ANY(tags))
                            AND (@sessionId::text IS NULL OR session_id = @sessionId::text)
            ORDER BY created_at DESC
            LIMIT @limit
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.Add(CreateNullableTextParameter("type", type));
        command.Parameters.Add(CreateNullableTextParameter("tag", tag));
        command.Parameters.Add(CreateNullableTextParameter("sessionId", sessionId));
        command.Parameters.AddWithValue("limit", limit);

        var rows = new List<MemoryRecord>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MemoryRecord(
                Id: reader.GetInt64(0),
                SessionId: reader.GetString(1),
                Type: reader.GetString(2),
                Content: reader.GetString(3),
                Tags: reader.GetFieldValue<string[]>(4),
                CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(5)));
        }

        return rows;
    }

    /// <summary>
    /// Performs semantic recall over memories using cosine similarity.
    /// </summary>
    /// <param name="queryEmbedding">Query embedding vector.</param>
    /// <param name="limit">Maximum number of rows to return.</param>
    /// <param name="type">Optional memory type filter.</param>
    /// <param name="tag">Optional tag filter.</param>
    /// <param name="sessionId">Optional session-id filter.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Semantically ranked memory rows.</returns>
    public async Task<IReadOnlyList<MemorySearchResult>> SearchMemoriesAsync(float[] queryEmbedding, int limit, string? type = null, string? tag = null, string? sessionId = null, CancellationToken cancellationToken = default)
    {
        if (queryEmbedding.Length == 0)
        {
            throw new ArgumentException("Query embedding cannot be empty.", nameof(queryEmbedding));
        }

        if (limit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(limit), "Limit must be greater than 0.");
        }

        const string sql = """
            SELECT id,
                   session_id,
                   type,
                   content,
                   tags,
                   created_at,
                   1 - (embedding <=> CAST(@embedding AS vector)) AS score
            FROM memories
                        WHERE (@type::text IS NULL OR type = @type::text)
                            AND (@tag::text IS NULL OR @tag::text = ANY(tags))
                            AND (@sessionId::text IS NULL OR session_id = @sessionId::text)
            ORDER BY embedding <=> CAST(@embedding AS vector)
            LIMIT @limit
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("embedding", ToVectorLiteral(queryEmbedding));
        command.Parameters.Add(CreateNullableTextParameter("type", type));
        command.Parameters.Add(CreateNullableTextParameter("tag", tag));
        command.Parameters.Add(CreateNullableTextParameter("sessionId", sessionId));
        command.Parameters.AddWithValue("limit", limit);

        var rows = new List<MemorySearchResult>();
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var scoreOrdinal = ResolveScoreOrdinal(reader);
        while (await reader.ReadAsync(cancellationToken))
        {
            var score = TryReadScore(reader, scoreOrdinal, fallbackOrdinal: 6);

            rows.Add(new MemorySearchResult(
                Id: reader.GetInt64(0),
                SessionId: reader.GetString(1),
                Type: reader.GetString(2),
                Content: reader.GetString(3),
                Tags: reader.GetFieldValue<string[]>(4),
                CreatedAtUtc: reader.GetFieldValue<DateTimeOffset>(5),
                Score: score));
        }

        return rows;
    }

    /// <summary>
    /// Computes memory analytics suitable for dashboard visualization.
    /// </summary>
    /// <param name="dayWindow">Number of trailing days to include in daily counts.</param>
    /// <param name="topTagLimit">Maximum number of top tags to return.</param>
    /// <param name="recentSessionLimit">Maximum number of recent sessions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Aggregated memory analytics payload.</returns>
    public async Task<MemoryDashboardStats> GetMemoryStatsAsync(int dayWindow = 30, int topTagLimit = 10, int recentSessionLimit = 12, CancellationToken cancellationToken = default)
    {
        if (dayWindow <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(dayWindow), "Day window must be greater than 0.");
        }

        if (topTagLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(topTagLimit), "Top tag limit must be greater than 0.");
        }

        if (recentSessionLimit <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(recentSessionLimit), "Recent session limit must be greater than 0.");
        }

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);

        var overview = await GetMemoryOverviewAsync(connection, cancellationToken);
        var typeCounts = await GetMemoryTypeCountsAsync(connection, cancellationToken);
        var topTags = await GetTopMemoryTagsAsync(connection, topTagLimit, cancellationToken);
        var dailyCounts = await GetMemoryDailyCountsAsync(connection, dayWindow, cancellationToken);
        var recentSessions = await GetRecentMemorySessionsAsync(connection, recentSessionLimit, cancellationToken);

        var canonicalTypeCounts = BuildCanonicalTypeCounts(typeCounts);
        return new MemoryDashboardStats(
            TotalMemories: overview.TotalMemories,
            Recent24HoursCount: overview.Recent24HoursCount,
            DistinctSessionCount: overview.DistinctSessionCount,
            AverageTagsPerMemory: overview.AverageTagsPerMemory,
            FirstMemoryAtUtc: overview.FirstMemoryAtUtc,
            LastMemoryAtUtc: overview.LastMemoryAtUtc,
            TypeCounts: canonicalTypeCounts,
            TopTags: topTags,
            DailyCounts: dailyCounts,
            RecentSessions: recentSessions);
    }

    /// <summary>
    /// Updates a memory entry and optionally recalculates embedding when content changes.
    /// </summary>
    /// <param name="memoryId">Memory row identifier.</param>
    /// <param name="type">Updated type, or <c>null</c> to keep existing value.</param>
    /// <param name="content">Updated content, or <c>null</c> to keep existing value.</param>
    /// <param name="tags">Updated tags, or <c>null</c> to keep existing tags.</param>
    /// <param name="embedding">Updated embedding, required when content changes.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when a row was updated, otherwise <c>false</c>.</returns>
    public async Task<bool> UpdateMemoryAsync(long memoryId, string? type, string? content, IReadOnlyList<string>? tags, IReadOnlyList<float>? embedding, CancellationToken cancellationToken = default)
    {
        if (memoryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryId), "Memory id must be greater than 0.");
        }

        if (content is not null && (embedding is null || embedding.Count == 0))
        {
            throw new ArgumentException("Embedding is required when content is updated.", nameof(embedding));
        }

        const string sql = """
            UPDATE memories
            SET type = COALESCE(@type::text, type),
                content = COALESCE(@content::text, content),
                embedding = CASE WHEN @embedding::text IS NULL THEN embedding ELSE CAST(@embedding AS vector) END,
                tags = COALESCE(@tags::text[], tags)
            WHERE id = @id
            """;

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", memoryId);
        command.Parameters.Add(CreateNullableTextParameter("type", type));
        command.Parameters.Add(CreateNullableTextParameter("content", content));
        command.Parameters.Add(CreateNullableTextParameter("embedding", embedding is null ? null : ToVectorLiteral(embedding)));

        var tagsParameter = new NpgsqlParameter("tags", NpgsqlDbType.Array | NpgsqlDbType.Text)
        {
            Value = tags is null ? DBNull.Value : tags.ToArray()
        };
        command.Parameters.Add(tagsParameter);

        var updated = await command.ExecuteNonQueryAsync(cancellationToken);
        return updated > 0;
    }

    /// <summary>
    /// Loads high-level aggregate counters for the memories table.
    /// </summary>
    /// <param name="connection">Open PostgreSQL connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Memory overview aggregate values.</returns>
    private static async Task<MemoryOverviewAggregate> GetMemoryOverviewAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT COUNT(*)::bigint AS total_memories,
                   COUNT(*) FILTER (WHERE created_at >= NOW() - INTERVAL '24 hours')::bigint AS recent_24h_count,
                   COUNT(DISTINCT session_id)::int AS distinct_session_count,
                   COALESCE(AVG(COALESCE(array_length(tags, 1), 0)), 0)::double precision AS average_tags_per_memory,
                   MIN(created_at) AS first_memory_at,
                   MAX(created_at) AS last_memory_at
            FROM memories
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        await reader.ReadAsync(cancellationToken);

        var firstMemoryAt = reader.IsDBNull(4) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(4);
        var lastMemoryAt = reader.IsDBNull(5) ? (DateTimeOffset?)null : reader.GetFieldValue<DateTimeOffset>(5);

        return new MemoryOverviewAggregate(
            TotalMemories: reader.GetInt64(0),
            Recent24HoursCount: reader.GetInt64(1),
            DistinctSessionCount: reader.GetInt32(2),
            AverageTagsPerMemory: reader.GetDouble(3),
            FirstMemoryAtUtc: firstMemoryAt,
            LastMemoryAtUtc: lastMemoryAt);
    }

    /// <summary>
    /// Loads grouped memory counts by type.
    /// </summary>
    /// <param name="connection">Open PostgreSQL connection.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Type-count rows from the memories table.</returns>
    private static async Task<IReadOnlyList<MemoryTypeCount>> GetMemoryTypeCountsAsync(NpgsqlConnection connection, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT type,
                   COUNT(*)::bigint AS memory_count
            FROM memories
            GROUP BY type
            ORDER BY type ASC
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MemoryTypeCount>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MemoryTypeCount(
                Type: reader.GetString(0),
                Count: reader.GetInt64(1)));
        }

        return rows;
    }

    /// <summary>
    /// Loads the most frequently used memory tags.
    /// </summary>
    /// <param name="connection">Open PostgreSQL connection.</param>
    /// <param name="limit">Maximum number of tags to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Top tag-count rows ordered by usage descending.</returns>
    private static async Task<IReadOnlyList<MemoryTagCount>> GetTopMemoryTagsAsync(NpgsqlConnection connection, int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT tag,
                   COUNT(*)::bigint AS tag_count
            FROM memories
            CROSS JOIN LATERAL unnest(tags) AS tag
            WHERE tag IS NOT NULL
              AND btrim(tag) <> ''
            GROUP BY tag
            ORDER BY tag_count DESC, tag ASC
            LIMIT @limit
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MemoryTagCount>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MemoryTagCount(
                Tag: reader.GetString(0),
                Count: reader.GetInt64(1)));
        }

        return rows;
    }

    /// <summary>
    /// Loads daily memory creation totals across a trailing date window.
    /// </summary>
    /// <param name="connection">Open PostgreSQL connection.</param>
    /// <param name="dayWindow">Trailing number of days to include.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Daily UTC date buckets with counts.</returns>
    private static async Task<IReadOnlyList<MemoryDailyCount>> GetMemoryDailyCountsAsync(NpgsqlConnection connection, int dayWindow, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT DATE(created_at AT TIME ZONE 'UTC') AS day_utc,
                   COUNT(*)::bigint AS memory_count
            FROM memories
            WHERE created_at >= NOW() - make_interval(days => @dayWindow)
            GROUP BY day_utc
            ORDER BY day_utc ASC
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("dayWindow", dayWindow);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MemoryDailyCount>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MemoryDailyCount(
                DateUtc: DateOnly.FromDateTime(reader.GetDateTime(0)),
                Count: reader.GetInt64(1)));
        }

        return rows;
    }

    /// <summary>
    /// Loads recently active sessions with memory volume and last-write timestamp.
    /// </summary>
    /// <param name="connection">Open PostgreSQL connection.</param>
    /// <param name="limit">Maximum number of sessions to return.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Recent session memory summaries.</returns>
    private static async Task<IReadOnlyList<MemorySessionSummary>> GetRecentMemorySessionsAsync(NpgsqlConnection connection, int limit, CancellationToken cancellationToken)
    {
        const string sql = """
            SELECT session_id,
                   COUNT(*)::bigint AS memory_count,
                   MAX(created_at) AS last_memory_at
            FROM memories
            GROUP BY session_id
            ORDER BY last_memory_at DESC
            LIMIT @limit
            """;

        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("limit", limit);
        await using var reader = await command.ExecuteReaderAsync(cancellationToken);
        var rows = new List<MemorySessionSummary>();

        while (await reader.ReadAsync(cancellationToken))
        {
            rows.Add(new MemorySessionSummary(
                SessionId: reader.GetString(0),
                MemoryCount: reader.GetInt64(1),
                LastMemoryAtUtc: reader.GetFieldValue<DateTimeOffset>(2)));
        }

        return rows;
    }

    /// <summary>
    /// Ensures all canonical memory types are present in the output, including zero-count entries.
    /// </summary>
    /// <param name="rawTypeCounts">Raw grouped type counts from the database.</param>
    /// <returns>Canonical type list ordered as episodic, semantic, procedural.</returns>
    private static IReadOnlyList<MemoryTypeCount> BuildCanonicalTypeCounts(IReadOnlyList<MemoryTypeCount> rawTypeCounts)
    {
        var countsByType = rawTypeCounts.ToDictionary(
            keySelector: row => row.Type,
            elementSelector: row => row.Count,
            comparer: StringComparer.OrdinalIgnoreCase);

        var canonicalTypes = new[] { "episodic", "semantic", "procedural" };
        return canonicalTypes
            .Select(type => new MemoryTypeCount(type, countsByType.GetValueOrDefault(type, 0)))
            .ToList();
    }

    /// <summary>
    /// Deletes a memory entry by its identifier.
    /// </summary>
    /// <param name="memoryId">Memory row identifier.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns><c>true</c> when a row was deleted, otherwise <c>false</c>.</returns>
    public async Task<bool> DeleteMemoryAsync(long memoryId, CancellationToken cancellationToken = default)
    {
        if (memoryId <= 0)
        {
            throw new ArgumentOutOfRangeException(nameof(memoryId), "Memory id must be greater than 0.");
        }

        const string sql = "DELETE FROM memories WHERE id = @id";

        await using var connection = new NpgsqlConnection(_connectionString);
        await connection.OpenAsync(cancellationToken);
        await using var command = new NpgsqlCommand(sql, connection);
        command.Parameters.AddWithValue("id", memoryId);

        var deleted = await command.ExecuteNonQueryAsync(cancellationToken);
        return deleted > 0;
    }

    /// <summary>
    /// Validates common memory insertion arguments.
    /// </summary>
    /// <param name="sessionId">Session identifier value.</param>
    /// <param name="type">Memory type value.</param>
    /// <param name="content">Content value.</param>
    /// <param name="embedding">Embedding value.</param>
    private static void ValidateMemoryArguments(string sessionId, string type, string content, IReadOnlyList<float> embedding)
    {
        if (string.IsNullOrWhiteSpace(sessionId))
        {
            throw new ArgumentException("Session id cannot be empty.", nameof(sessionId));
        }

        if (string.IsNullOrWhiteSpace(type))
        {
            throw new ArgumentException("Memory type cannot be empty.", nameof(type));
        }

        if (!string.Equals(type, "episodic", StringComparison.Ordinal) &&
            !string.Equals(type, "semantic", StringComparison.Ordinal) &&
            !string.Equals(type, "procedural", StringComparison.Ordinal))
        {
            throw new ArgumentException("Memory type must be episodic, semantic, or procedural.", nameof(type));
        }

        if (string.IsNullOrWhiteSpace(content))
        {
            throw new ArgumentException("Memory content cannot be empty.", nameof(content));
        }

        if (embedding.Count == 0)
        {
            throw new ArgumentException("Memory embedding cannot be empty.", nameof(embedding));
        }
    }

    /// <summary>
    /// Creates a nullable text parameter with an explicit PostgreSQL type.
    /// </summary>
    /// <param name="name">Parameter name.</param>
    /// <param name="value">Nullable string value.</param>
    /// <returns>Configured typed parameter for PostgreSQL commands.</returns>
    private static NpgsqlParameter CreateNullableTextParameter(string name, string? value)
    {
        return new NpgsqlParameter(name, NpgsqlDbType.Text)
        {
            Value = value is null ? DBNull.Value : value
        };
    }

    /// <summary>
    /// Resolves the ordinal for a projected <c>score</c> column when present.
    /// </summary>
    /// <param name="reader">Active data reader for the current query.</param>
    /// <returns>Resolved ordinal, or <c>null</c> when no <c>score</c> column exists.</returns>
    private static int? ResolveScoreOrdinal(NpgsqlDataReader reader)
    {
        try
        {
            return reader.GetOrdinal("score");
        }
        catch (IndexOutOfRangeException)
        {
            return null;
        }
    }

    /// <summary>
    /// Reads a score value from a result row with safe fallbacks for schema drift.
    /// </summary>
    /// <param name="reader">Active data reader for the current row.</param>
    /// <param name="scoreOrdinal">Optional ordinal resolved from the <c>score</c> alias.</param>
    /// <param name="fallbackOrdinal">Known positional fallback when alias lookup is unavailable.</param>
    /// <returns>Parsed score value, or <c>0</c> when no numeric score can be read.</returns>
    private static double TryReadScore(NpgsqlDataReader reader, int? scoreOrdinal, int fallbackOrdinal)
    {
        var ordinal = scoreOrdinal ?? fallbackOrdinal;
        if (ordinal < 0 || ordinal >= reader.FieldCount || reader.IsDBNull(ordinal))
        {
            return 0d;
        }

        return ConvertToScore(reader.GetValue(ordinal));
    }

    /// <summary>
    /// Converts known provider score value types to <see cref="double"/>.
    /// </summary>
    /// <param name="value">Raw provider value from the score column.</param>
    /// <returns>Converted score value, or <c>0</c> when the value is not numeric.</returns>
    private static double ConvertToScore(object value)
    {
        return value switch
        {
            double number => number,
            float number => number,
            decimal number => (double)number,
            short number => number,
            int number => number,
            long number => number,
            byte number => number,
            string text when double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out var parsed) => parsed,
            _ => 0d
        };
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

    /// <summary>
    /// In-memory representation of aggregate memory counters.
    /// </summary>
    /// <param name="TotalMemories">Total number of memory rows.</param>
    /// <param name="Recent24HoursCount">Number of rows created in the last 24 hours.</param>
    /// <param name="DistinctSessionCount">Distinct number of session identifiers.</param>
    /// <param name="AverageTagsPerMemory">Average tags attached per memory row.</param>
    /// <param name="FirstMemoryAtUtc">Oldest memory timestamp, when available.</param>
    /// <param name="LastMemoryAtUtc">Newest memory timestamp, when available.</param>
    private sealed record MemoryOverviewAggregate(
        long TotalMemories,
        long Recent24HoursCount,
        int DistinctSessionCount,
        double AverageTagsPerMemory,
        DateTimeOffset? FirstMemoryAtUtc,
        DateTimeOffset? LastMemoryAtUtc);
}
