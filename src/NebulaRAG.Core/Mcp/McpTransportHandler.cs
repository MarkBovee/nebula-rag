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
    private const string CreatePlanToolName = "create_plan";
    private const string GetPlanToolName = "get_plan";
    private const string ListPlansToolName = "list_plans";
    private const string UpdatePlanToolName = "update_plan";
    private const string CompleteTaskToolName = "complete_task";
    private const string UpdateTaskToolName = "update_task";
    private const string ArchivePlanToolName = "archive_plan";

    private readonly RagQueryService _queryService;
    private readonly RagManagementService _managementService;
    private readonly RagSourcesManifestService _sourcesManifestService;
    private readonly PostgresRagStore _store;
    private readonly TextChunker _chunker;
    private readonly IEmbeddingGenerator _embeddingGenerator;
    private readonly RagIndexer _indexer;
    private readonly RagSettings _settings;
    private readonly HttpClient _httpClient;
    private readonly IRuntimeTelemetrySink _telemetrySink;
    private readonly ILogger<McpTransportHandler> _logger;
    private readonly PostgresPlanStore _planStore;
    private readonly PlanService _planService;
    private readonly TaskService _taskService;
    private readonly SemaphoreSlim _planSchemaLock = new(1, 1);
    private bool _planSchemaInitialized;

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
    public McpTransportHandler(RagQueryService queryService, RagManagementService managementService, RagSourcesManifestService sourcesManifestService, PostgresRagStore store, TextChunker chunker, IEmbeddingGenerator embeddingGenerator, RagIndexer indexer, RagSettings settings, HttpClient httpClient, ILogger<McpTransportHandler> logger, IRuntimeTelemetrySink? telemetrySink = null)
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
        _telemetrySink = telemetrySink ?? new NullRuntimeTelemetrySink();
        _logger = logger;
        _planStore = new PostgresPlanStore(settings.Database.BuildConnectionString());
        _planService = new PlanService(_planStore);
        _taskService = new TaskService(_planStore);
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
                BuildToolDefinition(MemoryUpdateToolName, "Update one memory entry."),
                BuildToolDefinition(CreatePlanToolName, "Create a new plan with initial tasks."),
                BuildToolDefinition(GetPlanToolName, "Get a specific plan by ID."),
                BuildToolDefinition(ListPlansToolName, "List all plans for the current session."),
                BuildToolDefinition(UpdatePlanToolName, "Update plan details or status."),
                BuildToolDefinition(CompleteTaskToolName, "Complete a specific task."),
                BuildToolDefinition(UpdateTaskToolName, "Update a specific task."),
                BuildToolDefinition(ArchivePlanToolName, "Archive a plan.")
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
            ["inputSchema"] = BuildToolInputSchema(name)
        };
    }

    /// <summary>
    /// Builds an MCP input schema for a specific tool.
    /// </summary>
    /// <param name="toolName">Tool name.</param>
    /// <returns>JSON schema object for tool arguments.</returns>
    private static JsonObject BuildToolInputSchema(string toolName)
    {
        return toolName switch
        {
            QueryProjectRagToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["text"] = BuildStringSchema("Semantic query text."),
                    ["limit"] = BuildIntegerSchema("Optional max number of matches to return.", minimum: 1, maximum: 20)
                },
                "text"),
            RagIndexPathToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["sourcePath"] = BuildStringSchema("Source directory path to index."),
                    ["projectId"] = BuildStringSchema("Optional project id used as the source-key prefix.")
                },
                "sourcePath"),
            RagIndexTextToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["sourcePath"] = BuildStringSchema("Source key or file path for the text payload."),
                    ["content"] = BuildStringSchema("Text content to chunk and index."),
                    ["projectId"] = BuildStringSchema("Optional project id used as the source-key prefix.")
                },
                "sourcePath",
                "content"),
            RagIndexUrlToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["url"] = BuildStringSchema("HTTP(S) URL to fetch and index."),
                    ["sourcePath"] = BuildStringSchema("Optional source key override for stored content."),
                    ["projectId"] = BuildStringSchema("Optional project id used as the source-key prefix.")
                },
                "url"),
            RagReindexSourceToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["sourcePath"] = BuildStringSchema("Readable local file path to reindex."),
                    ["projectId"] = BuildStringSchema("Optional project id used as the source-key prefix.")
                },
                "sourcePath"),
            RagGetChunkToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["chunkId"] = BuildIntegerSchema("Chunk row identifier to fetch.", minimum: 1)
                },
                "chunkId"),
            RagSearchSimilarToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["text"] = BuildStringSchema("Semantic query text."),
                    ["limit"] = BuildIntegerSchema("Optional max number of matches to return.", minimum: 1, maximum: 20)
                },
                "text"),
            RagNormalizeSourcePathsToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["projectRootPath"] = BuildStringSchema("Optional project root path used for path normalization.")
                }),
            RagDeleteSourceToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["sourcePath"] = BuildStringSchema("Indexed source path to delete."),
                    ["confirm"] = BuildBooleanSchema("Safety flag. Must be true to delete.")
                },
                "sourcePath",
                "confirm"),
            RagPurgeAllToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["confirmPhrase"] = BuildConstStringSchema("Safety phrase required to purge all indexed data.", "PURGE ALL")
                },
                "confirmPhrase"),
            RagListSourcesToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["limit"] = BuildIntegerSchema("Optional max number of sources to return.", minimum: 1, maximum: 1000)
                }),
            MemoryStoreToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["sessionId"] = BuildStringSchema("Optional session-id for grouping related memories."),
                    ["projectId"] = BuildStringSchema("Optional project-id for project-wide memory grouping."),
                    ["type"] = BuildEnumStringSchema("Memory type.", "episodic", "semantic", "procedural"),
                    ["content"] = BuildStringSchema("Natural-language memory content to store."),
                    ["tags"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["description"] = "Optional list of tags for filtering.",
                        ["items"] = BuildStringSchema("Memory tag.")
                    }
                },
                "type",
                "content"),
            MemoryRecallToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["text"] = BuildStringSchema("Semantic probe text for memory recall."),
                    ["limit"] = BuildIntegerSchema("Optional max number of memories to return.", minimum: 1, maximum: 50),
                    ["type"] = BuildEnumStringSchema("Optional memory type filter.", "episodic", "semantic", "procedural"),
                    ["tag"] = BuildStringSchema("Optional memory tag filter."),
                    ["sessionId"] = BuildStringSchema("Optional session-id filter."),
                    ["projectId"] = BuildStringSchema("Optional project-id filter.")
                },
                "text"),
            MemoryListToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["limit"] = BuildIntegerSchema("Optional max number of memories to list.", minimum: 1, maximum: 100),
                    ["type"] = BuildEnumStringSchema("Optional memory type filter.", "episodic", "semantic", "procedural"),
                    ["tag"] = BuildStringSchema("Optional memory tag filter."),
                    ["sessionId"] = BuildStringSchema("Optional session-id filter."),
                    ["projectId"] = BuildStringSchema("Optional project-id filter.")
                }),
            MemoryDeleteToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["memoryId"] = BuildIntegerSchema("Memory identifier to delete.", minimum: 1)
                },
                "memoryId"),
            MemoryUpdateToolName => BuildObjectSchema(
                new JsonObject
                {
                    ["memoryId"] = BuildIntegerSchema("Memory identifier to update.", minimum: 1),
                    ["type"] = BuildEnumStringSchema("Optional memory type update.", "episodic", "semantic", "procedural"),
                    ["content"] = BuildStringSchema("Optional replacement content."),
                    ["tags"] = new JsonObject
                    {
                        ["type"] = "array",
                        ["description"] = "Optional replacement tags.",
                        ["items"] = BuildStringSchema("Memory tag.")
                    }
                },
                "memoryId"),
            CreatePlanToolName => BuildObjectSchema(
                    new JsonObject
                    {
                        ["sessionId"] = BuildStringSchema("Session ID for the plan."),
                        ["planName"] = BuildStringSchema("Name of the new plan."),
                        ["projectId"] = BuildStringSchema("Project ID for the plan."),
                        ["initialTasks"] = new JsonObject
                        {
                            ["type"] = "array",
                            ["description"] = "Initial tasks for the plan.",
                            ["items"] = BuildStringSchema("Task name.")
                        }
                    },
                    "sessionId",
                    "planName",
                    "projectId"),
                GetPlanToolName => BuildObjectSchema(
                    new JsonObject
                    {
                        ["sessionId"] = BuildStringSchema("Optional session ID override for audit metadata."),
                        ["planId"] = BuildIntegerSchema("ID of the plan to retrieve.", minimum: 1)
                    },
                    "planId"),
                ListPlansToolName => BuildObjectSchema(
                    new JsonObject
                    {
                        ["sessionId"] = BuildStringSchema("Session ID to list plans for.")
                    },
                    "sessionId"),
                UpdatePlanToolName => BuildObjectSchema(
                    new JsonObject
                    {
                        ["sessionId"] = BuildStringSchema("Optional session ID override for audit metadata."),
                        ["planId"] = BuildIntegerSchema("ID of the plan to update.", minimum: 1),
                        ["planName"] = BuildStringSchema("New name for the plan (optional)."),
                        ["status"] = BuildStringSchema("New status for the plan (optional).")
                    },
                    "planId"),
                CompleteTaskToolName => BuildObjectSchema(
                    new JsonObject
                    {
                        ["sessionId"] = BuildStringSchema("Optional session ID override for audit metadata."),
                        ["planId"] = BuildIntegerSchema("ID of the plan containing the task.", minimum: 1),
                        ["taskId"] = BuildIntegerSchema("ID of the task to complete.", minimum: 1)
                    },
                    "planId",
                    "taskId"),
                UpdateTaskToolName => BuildObjectSchema(
                    new JsonObject
                    {
                        ["sessionId"] = BuildStringSchema("Optional session ID override for audit metadata."),
                        ["planId"] = BuildIntegerSchema("ID of the plan containing the task.", minimum: 1),
                        ["taskId"] = BuildIntegerSchema("ID of the task to update.", minimum: 1),
                        ["taskName"] = BuildStringSchema("New name for the task (optional)."),
                        ["status"] = BuildStringSchema("New status for the task (optional).")
                    },
                    "planId",
                    "taskId"),
                ArchivePlanToolName => BuildObjectSchema(
                    new JsonObject
                    {
                        ["sessionId"] = BuildStringSchema("Optional session ID override for audit metadata."),
                        ["planId"] = BuildIntegerSchema("ID of the plan to archive.", minimum: 1)
                    },
                    "planId"),
                _ => BuildObjectSchema(new JsonObject())
        };
    }

    /// <summary>
    /// Builds a JSON object schema node.
    /// </summary>
    /// <param name="properties">Schema properties.</param>
    /// <param name="required">Optional required property names.</param>
    /// <returns>Object schema node.</returns>
    private static JsonObject BuildObjectSchema(JsonObject properties, params string[] required)
    {
        var schema = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = properties
        };

        if (required.Length > 0)
        {
            var requiredArray = new JsonArray();
            foreach (var item in required)
            {
                requiredArray.Add(item);
            }

            schema["required"] = requiredArray;
        }

        return schema;
    }

    /// <summary>
    /// Builds a string-property schema node with description.
    /// </summary>
    /// <param name="description">Property description.</param>
    /// <returns>String schema node.</returns>
    private static JsonObject BuildStringSchema(string description)
    {
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description
        };
    }

    /// <summary>
    /// Builds a string-property schema node with enum constraints.
    /// </summary>
    /// <param name="description">Property description.</param>
    /// <param name="values">Allowed string values.</param>
    /// <returns>String enum schema node.</returns>
    private static JsonObject BuildEnumStringSchema(string description, params string[] values)
    {
        var enumValues = new JsonArray();
        foreach (var value in values)
        {
            enumValues.Add(value);
        }

        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["enum"] = enumValues
        };
    }

    /// <summary>
    /// Builds a string-property schema node constrained to one constant value.
    /// </summary>
    /// <param name="description">Property description.</param>
    /// <param name="value">Required constant string value.</param>
    /// <returns>Constant string schema node.</returns>
    private static JsonObject BuildConstStringSchema(string description, string value)
    {
        return new JsonObject
        {
            ["type"] = "string",
            ["description"] = description,
            ["const"] = value
        };
    }

    /// <summary>
    /// Builds a boolean-property schema node with description.
    /// </summary>
    /// <param name="description">Property description.</param>
    /// <returns>Boolean schema node.</returns>
    private static JsonObject BuildBooleanSchema(string description)
    {
        return new JsonObject
        {
            ["type"] = "boolean",
            ["description"] = description
        };
    }

    /// <summary>
    /// Builds an integer-property schema node with description and optional bounds.
    /// </summary>
    /// <param name="description">Property description.</param>
    /// <param name="minimum">Optional inclusive minimum.</param>
    /// <param name="maximum">Optional inclusive maximum.</param>
    /// <returns>Integer schema node.</returns>
    private static JsonObject BuildIntegerSchema(string description, int? minimum = null, int? maximum = null)
    {
        var schema = new JsonObject
        {
            ["type"] = "integer",
            ["description"] = description
        };

        if (minimum.HasValue)
        {
            schema["minimum"] = minimum.Value;
        }

        if (maximum.HasValue)
        {
            schema["maximum"] = maximum.Value;
        }

        return schema;
    }

}
