using System.Text.Json.Nodes;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;

namespace NebulaRAG.Core.Mcp;

/// <summary>
/// Handles MCP JSON-RPC transport methods and tool execution.
/// </summary>
public sealed partial class McpTransportHandler
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
    private const string RagNormalizeSourcePathsToolName = "rag_normalize_source_paths";
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
    /// <param name="managementService">Management service for stats, health, and source tools.</param>
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

        if (method.StartsWith("notifications/", StringComparison.Ordinal))
        {
            return BuildResult(id, new JsonObject());
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
                BuildToolDefinition(RagNormalizeSourcePathsToolName, "Normalize stored source paths and remove duplicates after key changes."),
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
}
