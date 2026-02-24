using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.AddonHost.Mcp;

/// <summary>
/// Handles MCP JSON-RPC transport methods and tool execution for the add-on host.
/// </summary>
public sealed class McpTransportHandler
{
    private const string QueryProjectRagToolName = "query_project_rag";
    private const string RagInitSchemaToolName = "rag_init_schema";
    private const string RagHealthCheckToolName = "rag_health_check";
    private const string RagServerInfoToolName = "rag_server_info";
    private const string RagIndexStatsToolName = "rag_index_stats";
    private const string RagListSourcesToolName = "rag_list_sources";
    private const string RagIndexPathToolName = "rag_index_path";
    private const string RagIndexTextToolName = "rag_index_text";
    private const string RagIndexUrlToolName = "rag_index_url";
    private const string RagReindexSourceToolName = "rag_reindex_source";
    private const string RagGetChunkToolName = "rag_get_chunk";
    private const string RagSearchSimilarToolName = "rag_search_similar";
    private const string RagDeleteSourceToolName = "rag_delete_source";
    private const string RagPurgeAllToolName = "rag_purge_all";
    private const string MemoryStoreToolName = "memory_store";
    private const string MemoryRecallToolName = "memory_recall";
    private const string MemoryListToolName = "memory_list";
    private const string MemoryDeleteToolName = "memory_delete";
    private const string MemoryUpdateToolName = "memory_update";

    private readonly RagQueryService _queryService;
    private readonly RagManagementService _managementService;
    private readonly RagSourcesManifestService _sourcesManifestService;
    private readonly PostgresRagStore _store;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RagIndexer _indexer;
    private readonly RagSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly ILogger<McpTransportHandler> _logger;

    /// <summary>
    /// Initializes a new instance of the <see cref="McpTransportHandler"/> class.
    /// </summary>
    /// <param name="queryService">Query service for semantic search tools.</param>
    /// <param name="managementService">Management service for stats/health/source tools.</param>
    /// <param name="sourcesManifestService">Manifest service for rag-sources synchronization.</param>
    /// <param name="store">Store for schema and chunk/memory operations.</param>
    /// <param name="chunker">Chunker for direct text indexing tools.</param>
    /// <param name="embeddingGenerator">Embedding generator for query and memory tools.</param>
    /// <param name="indexer">Indexer for path-based indexing.</param>
    /// <param name="settings">Runtime settings.</param>
    /// <param name="httpClient">HTTP client for URL ingestion.</param>
    /// <param name="logger">Handler logger.</param>
    public McpTransportHandler(RagQueryService queryService, RagManagementService managementService, RagSourcesManifestService sourcesManifestService, PostgresRagStore store, TextChunker chunker, IEmbeddingGenerator embeddingGenerator, RagIndexer indexer, RagSettings settings, HttpClient httpClient, ILogger<McpTransportHandler> logger)
    {
        _queryService = queryService;
        _managementService = managementService;
        _sourcesManifestService = sourcesManifestService;
        _store = store;
        _chunker = chunker;
        _embeddingGenerator = embeddingGenerator;
        _indexer = indexer;
        _settings = settings;
        _httpClient = httpClient;
        _logger = logger;
    }

    /// <summary>
    /// Handles one MCP JSON-RPC request and returns a JSON-RPC response envelope.
    /// </summary>
    /// <param name="request">Incoming request payload.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>JSON-RPC response object.</returns>
    public async Task<JsonObject> HandleAsync(JsonObject request, CancellationToken cancellationToken)
    {
        var method = request["method"]?.GetValue<string>();
        var id = request["id"]?.DeepClone();
        var parameters = request["params"]?.AsObject();

        if (string.IsNullOrWhiteSpace(method))
        {
            return BuildError(id, -32600, "Invalid request");
        }

        if (method == "initialize")
        {
            return BuildResult(id, new JsonObject
            {
                ["protocolVersion"] = "2025-11-25",
                ["serverInfo"] = new JsonObject { ["name"] = "Nebula RAG", ["version"] = "0.2.0" },
                ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } }
            });
        }

        if (method == "ping")
        {
            return BuildResult(id, new JsonObject());
        }

        if (method == "tools/list")
        {
            return BuildResult(id, BuildToolsList());
        }

        if (method != "tools/call")
        {
            return BuildError(id, -32601, $"Method not found: {method}");
        }

        var toolName = parameters?["name"]?.GetValue<string>();
        var arguments = parameters?["arguments"]?.AsObject();
        if (string.IsNullOrWhiteSpace(toolName))
        {
            return BuildError(id, -32602, "Missing tool name");
        }

        var result = await ExecuteToolAsync(toolName, arguments, cancellationToken);
        return BuildResult(id, result);
    }

    /// <summary>
    /// Builds the tools/list payload.
    /// </summary>
    /// <returns>Tool catalog object.</returns>
    private static JsonObject BuildToolsList()
    {
        return new JsonObject
        {
            ["tools"] = new JsonArray
            {
                BuildToolDefinition(RagInitSchemaToolName, "Initialize Nebula RAG schema."),
                BuildToolDefinition(QueryProjectRagToolName, "Query Nebula RAG indexed context."),
                BuildToolDefinition(RagHealthCheckToolName, "Run health checks."),
                BuildToolDefinition(RagServerInfoToolName, "Get runtime server details."),
                BuildToolDefinition(RagIndexStatsToolName, "Get index statistics."),
                BuildToolDefinition(RagListSourcesToolName, "List indexed sources."),
                BuildToolDefinition(RagIndexPathToolName, "Index a source directory path."),
                BuildToolDefinition(RagIndexTextToolName, "Index direct text content under a source key."),
                BuildToolDefinition(RagIndexUrlToolName, "Fetch URL content and index it."),
                BuildToolDefinition(RagReindexSourceToolName, "Reindex an existing source path from disk."),
                BuildToolDefinition(RagGetChunkToolName, "Get one indexed chunk by chunk id."),
                BuildToolDefinition(RagSearchSimilarToolName, "Run similarity search without project path filtering."),
                BuildToolDefinition(RagDeleteSourceToolName, "Delete one indexed source path."),
                BuildToolDefinition(RagPurgeAllToolName, "Purge all indexed content."),
                BuildToolDefinition(MemoryStoreToolName, "Store one memory observation with tags and type."),
                BuildToolDefinition(MemoryRecallToolName, "Recall semantically similar memories."),
                BuildToolDefinition(MemoryListToolName, "List recent memories with optional filters."),
                BuildToolDefinition(MemoryDeleteToolName, "Delete one memory by id."),
                BuildToolDefinition(MemoryUpdateToolName, "Update one memory entry.")
            }
        };
    }

    /// <summary>
    /// Builds one tool definition entry.
    /// </summary>
    /// <param name="name">Tool name.</param>
    /// <param name="description">Tool description.</param>
    /// <returns>Tool definition object.</returns>
    private static JsonObject BuildToolDefinition(string name, string description)
    {
        return new JsonObject
        {
            ["name"] = name,
            ["description"] = description,
            ["inputSchema"] = new JsonObject
            {
                ["type"] = "object",
                ["properties"] = new JsonObject()
            }
        };
    }

    /// <summary>
    /// Executes a requested MCP tool.
    /// </summary>
    /// <param name="toolName">Tool name.</param>
    /// <param name="arguments">Tool arguments.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>MCP tool result payload.</returns>
    private async Task<JsonObject> ExecuteToolAsync(string toolName, JsonObject? arguments, CancellationToken cancellationToken)
    {
        try
        {
            if (toolName == RagInitSchemaToolName)
            {
                await _store.InitializeSchemaAsync(_settings.Ingestion.VectorDimensions, cancellationToken);
                var manifestSyncResult = await TrySyncRagSourcesManifestAsync(null, cancellationToken);
                return BuildToolResult("Schema initialized.", new JsonObject
                {
                    ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
                    ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
                });
            }

            if (toolName == QueryProjectRagToolName)
            {
                var text = arguments?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return BuildToolResult("Missing required argument: text", isError: true);
                }

                var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? _settings.Retrieval.DefaultTopK, 1, 20);
                var results = await _queryService.QueryAsync(text, limit, cancellationToken);
                var items = new JsonArray();
                foreach (var item in results)
                {
                    items.Add(new JsonObject
                    {
                        ["sourcePath"] = item.SourcePath,
                        ["chunkIndex"] = item.ChunkIndex,
                        ["score"] = item.Score,
                        ["snippet"] = TrimSnippet(item.ChunkText)
                    });
                }

                return BuildToolResult($"Found {results.Count} matches.", new JsonObject
                {
                    ["query"] = text,
                    ["limit"] = limit,
                    ["matches"] = items
                });
            }

            if (toolName == RagHealthCheckToolName)
            {
                var health = await _managementService.HealthCheckAsync(cancellationToken);
                return BuildToolResult(health.Message, new JsonObject { ["isHealthy"] = health.IsHealthy }, isError: !health.IsHealthy);
            }

            if (toolName == RagServerInfoToolName)
            {
                return BuildToolResult("Server info.", new JsonObject
                {
                    ["serverName"] = "Nebula RAG",
                    ["databaseHost"] = _settings.Database.Host,
                    ["databasePort"] = _settings.Database.Port,
                    ["databaseName"] = _settings.Database.Database
                });
            }

            if (toolName == RagIndexStatsToolName)
            {
                var stats = await _managementService.GetStatsAsync(cancellationToken: cancellationToken);
                return BuildToolResult("Index stats.", new JsonObject
                {
                    ["documentCount"] = stats.DocumentCount,
                    ["chunkCount"] = stats.ChunkCount,
                    ["totalTokens"] = stats.TotalTokens
                });
            }

            if (toolName == RagListSourcesToolName)
            {
                var sources = await _managementService.ListSourcesAsync(cancellationToken: cancellationToken);
                var sourceItems = new JsonArray();
                foreach (var source in sources)
                {
                    sourceItems.Add(new JsonObject
                    {
                        ["sourcePath"] = source.SourcePath,
                        ["chunkCount"] = source.ChunkCount,
                        ["indexedAt"] = source.IndexedAt.ToUniversalTime().ToString("O")
                    });
                }

                return BuildToolResult("Indexed sources.", new JsonObject { ["items"] = sourceItems });
            }

            if (toolName == RagIndexPathToolName)
            {
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return BuildToolResult("Missing required argument: sourcePath", isError: true);
                }

                var summary = await _indexer.IndexDirectoryAsync(sourcePath, cancellationToken);
                var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcePath, cancellationToken);
                return BuildToolResult("Index complete.", new JsonObject
                {
                    ["documentsIndexed"] = summary.DocumentsIndexed,
                    ["documentsSkipped"] = summary.DocumentsSkipped,
                    ["chunksIndexed"] = summary.ChunksIndexed,
                    ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
                    ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
                });
            }

            if (toolName == RagIndexTextToolName)
            {
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                var content = arguments?["content"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(sourcePath) || string.IsNullOrWhiteSpace(content))
                {
                    return BuildToolResult("sourcePath and content are required.", isError: true);
                }

                var chunks = _chunker.Chunk(content, _settings.Ingestion.ChunkSize, _settings.Ingestion.ChunkOverlap);
                if (chunks.Count == 0)
                {
                    return BuildToolResult("No indexable chunks produced.", isError: true);
                }

                var chunkEmbeddings = chunks.Select(chunk => new ChunkEmbedding(chunk.Index, chunk.Text, chunk.TokenCount, _embeddingGenerator.GenerateEmbedding(chunk.Text, _settings.Ingestion.VectorDimensions))).ToList();
                var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content)));
                var updated = await _store.UpsertDocumentAsync(sourcePath, contentHash, chunkEmbeddings, cancellationToken);
                var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcePath, cancellationToken);

                return BuildToolResult(updated ? "Source text indexed." : "Source text unchanged.", new JsonObject
                {
                    ["sourcePath"] = sourcePath,
                    ["updated"] = updated,
                    ["chunkCount"] = chunkEmbeddings.Count,
                    ["contentHash"] = contentHash,
                    ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
                    ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
                });
            }

            if (toolName == RagIndexUrlToolName)
            {
                var url = arguments?["url"]?.GetValue<string>();
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(url))
                {
                    return BuildToolResult("url is required.", isError: true);
                }

                var fetchedContent = await _httpClient.GetStringAsync(url, cancellationToken);
                var targetSourcePath = string.IsNullOrWhiteSpace(sourcePath) ? url : sourcePath;
                var indexArgs = new JsonObject
                {
                    ["sourcePath"] = targetSourcePath,
                    ["content"] = fetchedContent
                };

                return await ExecuteToolAsync(RagIndexTextToolName, indexArgs, cancellationToken);
            }

            if (toolName == RagReindexSourceToolName)
            {
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(sourcePath))
                {
                    return BuildToolResult("sourcePath is required.", isError: true);
                }

                if (!File.Exists(sourcePath))
                {
                    return BuildToolResult("sourcePath must point to a readable file for reindex.", isError: true);
                }

                var content = await File.ReadAllTextAsync(sourcePath, cancellationToken);
                var reindexArgs = new JsonObject { ["sourcePath"] = sourcePath, ["content"] = content };
                return await ExecuteToolAsync(RagIndexTextToolName, reindexArgs, cancellationToken);
            }

            if (toolName == RagGetChunkToolName)
            {
                var chunkId = arguments?["chunkId"]?.GetValue<long?>();
                if (chunkId is null || chunkId.Value <= 0)
                {
                    return BuildToolResult("chunkId is required and must be > 0.", isError: true);
                }

                var chunk = await _store.GetChunkByIdAsync(chunkId.Value, cancellationToken);
                if (chunk is null)
                {
                    return BuildToolResult("Chunk not found.", isError: true);
                }

                return BuildToolResult("Chunk retrieved.", new JsonObject
                {
                    ["chunkId"] = chunk.ChunkId,
                    ["sourcePath"] = chunk.SourcePath,
                    ["chunkIndex"] = chunk.ChunkIndex,
                    ["tokenCount"] = chunk.TokenCount,
                    ["indexedAtUtc"] = chunk.IndexedAtUtc.ToUniversalTime().ToString("O"),
                    ["chunkText"] = chunk.ChunkText
                });
            }

            if (toolName == RagSearchSimilarToolName)
            {
                var text = arguments?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return BuildToolResult("text is required.", isError: true);
                }

                var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? _settings.Retrieval.DefaultTopK, 1, 20);
                var results = await _queryService.QueryAsync(text, limit, cancellationToken);
                var matches = new JsonArray();
                foreach (var item in results)
                {
                    matches.Add(new JsonObject
                    {
                        ["sourcePath"] = item.SourcePath,
                        ["chunkIndex"] = item.ChunkIndex,
                        ["score"] = item.Score,
                        ["snippet"] = TrimSnippet(item.ChunkText)
                    });
                }

                return BuildToolResult($"Found {results.Count} similar chunks.", new JsonObject
                {
                    ["query"] = text,
                    ["limit"] = limit,
                    ["matches"] = matches
                });
            }

            if (toolName == RagDeleteSourceToolName)
            {
                var sourcePath = arguments?["sourcePath"]?.GetValue<string>();
                var confirm = arguments?["confirm"]?.GetValue<bool>() == true;
                if (string.IsNullOrWhiteSpace(sourcePath) || !confirm)
                {
                    return BuildToolResult("sourcePath and confirm=true are required.", isError: true);
                }

                var deleted = await _managementService.DeleteSourceAsync(sourcePath, cancellationToken);
                var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcePath, cancellationToken);
                return BuildToolResult($"Deleted {deleted} items.", new JsonObject
                {
                    ["deleted"] = deleted,
                    ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
                    ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
                });
            }

            if (toolName == RagPurgeAllToolName)
            {
                var phrase = arguments?["confirmPhrase"]?.GetValue<string>();
                if (!string.Equals(phrase, "PURGE ALL", StringComparison.Ordinal))
                {
                    return BuildToolResult("confirmPhrase must be PURGE ALL", isError: true);
                }

                await _managementService.PurgeAllAsync(cancellationToken);
                var manifestSyncResult = await TrySyncRagSourcesManifestAsync(null, cancellationToken);
                return BuildToolResult("Purge complete.", new JsonObject
                {
                    ["sourcesManifestPath"] = manifestSyncResult?.ManifestPath,
                    ["sourcesManifestSourceCount"] = manifestSyncResult?.SourceCount
                });
            }

            if (toolName == MemoryStoreToolName)
            {
                var sessionId = arguments?["sessionId"]?.GetValue<string>();
                var type = arguments?["type"]?.GetValue<string>();
                var content = arguments?["content"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(sessionId) || string.IsNullOrWhiteSpace(type) || string.IsNullOrWhiteSpace(content))
                {
                    return BuildToolResult("sessionId, type and content are required.", isError: true);
                }

                var tags = ParseTags(arguments?["tags"]);
                var embedding = _embeddingGenerator.GenerateEmbedding(content, _settings.Ingestion.VectorDimensions);
                var memoryId = await _store.CreateMemoryAsync(sessionId, type, content, tags, embedding, cancellationToken);

                return BuildToolResult("Memory stored.", new JsonObject
                {
                    ["memoryId"] = memoryId,
                    ["sessionId"] = sessionId,
                    ["type"] = type,
                    ["tags"] = JsonSerializer.SerializeToNode(tags)
                });
            }

            if (toolName == MemoryRecallToolName)
            {
                var text = arguments?["text"]?.GetValue<string>();
                if (string.IsNullOrWhiteSpace(text))
                {
                    return BuildToolResult("text is required.", isError: true);
                }

                var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? 10, 1, 50);
                var type = arguments?["type"]?.GetValue<string>();
                var tag = arguments?["tag"]?.GetValue<string>();
                var queryEmbedding = _embeddingGenerator.GenerateEmbedding(text, _settings.Ingestion.VectorDimensions);
                var memories = await _store.SearchMemoriesAsync(queryEmbedding, limit, type, tag, cancellationToken);
                var items = new JsonArray();
                foreach (var memory in memories)
                {
                    items.Add(new JsonObject
                    {
                        ["id"] = memory.Id,
                        ["sessionId"] = memory.SessionId,
                        ["type"] = memory.Type,
                        ["content"] = memory.Content,
                        ["tags"] = JsonSerializer.SerializeToNode(memory.Tags),
                        ["createdAtUtc"] = memory.CreatedAtUtc.ToUniversalTime().ToString("O"),
                        ["score"] = memory.Score
                    });
                }

                return BuildToolResult($"Recalled {memories.Count} memories.", new JsonObject { ["items"] = items });
            }

            if (toolName == MemoryListToolName)
            {
                var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? 20, 1, 100);
                var type = arguments?["type"]?.GetValue<string>();
                var tag = arguments?["tag"]?.GetValue<string>();
                var memories = await _store.ListMemoriesAsync(limit, type, tag, cancellationToken);
                var items = new JsonArray();
                foreach (var memory in memories)
                {
                    items.Add(new JsonObject
                    {
                        ["id"] = memory.Id,
                        ["sessionId"] = memory.SessionId,
                        ["type"] = memory.Type,
                        ["content"] = memory.Content,
                        ["tags"] = JsonSerializer.SerializeToNode(memory.Tags),
                        ["createdAtUtc"] = memory.CreatedAtUtc.ToUniversalTime().ToString("O")
                    });
                }

                return BuildToolResult($"Listed {memories.Count} memories.", new JsonObject { ["items"] = items });
            }

            if (toolName == MemoryDeleteToolName)
            {
                var memoryId = arguments?["memoryId"]?.GetValue<long?>();
                if (memoryId is null || memoryId.Value <= 0)
                {
                    return BuildToolResult("memoryId is required and must be > 0.", isError: true);
                }

                var deleted = await _store.DeleteMemoryAsync(memoryId.Value, cancellationToken);
                return BuildToolResult(deleted ? "Memory deleted." : "Memory not found.", new JsonObject { ["deleted"] = deleted });
            }

            if (toolName == MemoryUpdateToolName)
            {
                var memoryId = arguments?["memoryId"]?.GetValue<long?>();
                if (memoryId is null || memoryId.Value <= 0)
                {
                    return BuildToolResult("memoryId is required and must be > 0.", isError: true);
                }

                var type = arguments?["type"]?.GetValue<string>();
                var content = arguments?["content"]?.GetValue<string>();
                var tagsNode = arguments?["tags"];
                var tags = tagsNode is null ? null : ParseTags(tagsNode);
                var embedding = string.IsNullOrWhiteSpace(content) ? null : _embeddingGenerator.GenerateEmbedding(content, _settings.Ingestion.VectorDimensions);

                var updated = await _store.UpdateMemoryAsync(memoryId.Value, type, content, tags, embedding, cancellationToken);
                return BuildToolResult(updated ? "Memory updated." : "Memory not found.", new JsonObject { ["updated"] = updated });
            }

            return BuildToolResult($"Unknown tool: {toolName}", isError: true);
        }
        catch (Exception ex)
        {
            return BuildToolResult($"Tool execution failed: {ex.Message}", isError: true);
        }
    }

    /// <summary>
    /// Builds a JSON-RPC success envelope.
    /// </summary>
    /// <param name="id">Request ID.</param>
    /// <param name="result">Result payload.</param>
    /// <returns>JSON-RPC success object.</returns>
    private static JsonObject BuildResult(JsonNode? id, JsonObject result)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["result"] = result
        };
    }

    /// <summary>
    /// Builds a JSON-RPC error envelope.
    /// </summary>
    /// <param name="id">Request ID.</param>
    /// <param name="code">Error code.</param>
    /// <param name="message">Error message.</param>
    /// <returns>JSON-RPC error object.</returns>
    private static JsonObject BuildError(JsonNode? id, int code, string message)
    {
        return new JsonObject
        {
            ["jsonrpc"] = "2.0",
            ["id"] = id,
            ["error"] = new JsonObject
            {
                ["code"] = code,
                ["message"] = message
            }
        };
    }

    /// <summary>
    /// Builds MCP tool result payload.
    /// </summary>
    /// <param name="text">Result text.</param>
    /// <param name="structuredContent">Optional structured content.</param>
    /// <param name="isError">Whether the result is an error.</param>
    /// <returns>MCP tool result payload.</returns>
    private static JsonObject BuildToolResult(string text, JsonObject? structuredContent = null, bool isError = false)
    {
        var result = new JsonObject
        {
            ["content"] = new JsonArray
            {
                new JsonObject
                {
                    ["type"] = "text",
                    ["text"] = text
                }
            }
        };

        if (structuredContent is not null)
        {
            result["structuredContent"] = structuredContent;
        }

        if (isError)
        {
            result["isError"] = true;
        }

        return result;
    }

    /// <summary>
    /// Tries to sync rag-sources manifest while keeping tool execution resilient.
    /// </summary>
    /// <param name="contextPath">Optional context path.</param>
    /// <param name="cancellationToken">Cancellation token.</param>
    /// <returns>Sync result or null on failure.</returns>
    private async Task<RagSourcesManifestSyncResult?> TrySyncRagSourcesManifestAsync(string? contextPath, CancellationToken cancellationToken)
    {
        try
        {
            return await _sourcesManifestService.SyncAsync(contextPath, cancellationToken);
        }
        catch (Exception ex)
        {
            _logger.LogWarning(ex, "Failed to synchronize rag-sources manifest after successful index operation.");
            return null;
        }
    }

    /// <summary>
    /// Parses string tags from JSON node.
    /// </summary>
    /// <param name="tagsNode">Tags JSON node.</param>
    /// <returns>Normalized tags list.</returns>
    private static List<string> ParseTags(JsonNode? tagsNode)
    {
        if (tagsNode is not JsonArray tagsArray)
        {
            return [];
        }

        var tags = new List<string>();
        foreach (var item in tagsArray)
        {
            var tag = item?.GetValue<string>();
            if (string.IsNullOrWhiteSpace(tag))
            {
                continue;
            }

            tags.Add(tag.Trim());
        }

        return tags;
    }

    /// <summary>
    /// Trims chunk text for compact MCP output snippets.
    /// </summary>
    /// <param name="source">Source text.</param>
    /// <returns>Trimmed and truncated snippet.</returns>
    private static string TrimSnippet(string source)
    {
        var flattened = source.Replace('\r', ' ').Replace('\n', ' ').Trim();
        return flattened.Length <= 280 ? flattened : $"{flattened[..280]}...";
    }
}
