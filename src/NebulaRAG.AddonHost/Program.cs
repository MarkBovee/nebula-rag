using System.Text;
using System.Text.Json;
using System.Text.Json.Nodes;
using Microsoft.AspNetCore.Http.Json;
using Microsoft.Extensions.Configuration;
using Microsoft.Extensions.Logging;
using NebulaRAG.Core.Chunking;
using NebulaRAG.Core.Configuration;
using NebulaRAG.Core.Embeddings;
using NebulaRAG.Core.Models;
using NebulaRAG.Core.Services;
using NebulaRAG.Core.Storage;
using Serilog;
using Serilog.Events;

const string QueryProjectRagToolName = "query_project_rag";
const string RagInitSchemaToolName = "rag_init_schema";
const string RagHealthCheckToolName = "rag_health_check";
const string RagServerInfoToolName = "rag_server_info";
const string RagIndexStatsToolName = "rag_index_stats";
const string RagListSourcesToolName = "rag_list_sources";
const string RagIndexPathToolName = "rag_index_path";
const string RagIndexTextToolName = "rag_index_text";
const string RagIndexUrlToolName = "rag_index_url";
const string RagReindexSourceToolName = "rag_reindex_source";
const string RagGetChunkToolName = "rag_get_chunk";
const string RagSearchSimilarToolName = "rag_search_similar";
const string RagDeleteSourceToolName = "rag_delete_source";
const string RagPurgeAllToolName = "rag_purge_all";
const string MemoryStoreToolName = "memory_store";
const string MemoryRecallToolName = "memory_recall";
const string MemoryListToolName = "memory_list";
const string MemoryDeleteToolName = "memory_delete";
const string MemoryUpdateToolName = "memory_update";

var httpClient = new HttpClient();

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .MinimumLevel.Override("Microsoft", LogEventLevel.Warning)
    .MinimumLevel.Override("Microsoft.AspNetCore", LogEventLevel.Warning)
    .MinimumLevel.Override("System", LogEventLevel.Warning)
    .Enrich.FromLogContext()
    .WriteTo.Console(outputTemplate: "[{Timestamp:HH:mm:ss} {Level:u3}] {Message:lj}{NewLine}{Exception}")
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.WriteIndented = true;
});

var settings = LoadSettings();
settings.Validate();
var pathBase = NormalizePathBase(Environment.GetEnvironmentVariable("NEBULARAG_PathBase"));

var loggerFactory = LoggerFactory.Create(loggingBuilder =>
{
    loggingBuilder.AddSerilog();
    loggingBuilder.SetMinimumLevel(LogLevel.Information);
});

// Initialize core services: PostgreSQL store, text chunker, embedding generator, query and management services, and indexer.
var store = new PostgresRagStore(settings.Database.BuildConnectionString());
var chunker = new TextChunker();
var embeddingGenerator = new HashEmbeddingGenerator();
var queryService = new RagQueryService(store, embeddingGenerator, settings, loggerFactory.CreateLogger<RagQueryService>());
var managementService = new RagManagementService(store, loggerFactory.CreateLogger<RagManagementService>());
var sourcesManifestService = new RagSourcesManifestService(store, settings, loggerFactory.CreateLogger<RagSourcesManifestService>());
var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, loggerFactory.CreateLogger<RagIndexer>());

await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions);

var app = builder.Build();
Log.Information("NebulaRAG add-on ignition sequence started.");

if (!string.IsNullOrEmpty(pathBase))
{
    Log.Information("Navigation corridor locked to path base {PathBase}", pathBase);
    app.UsePathBase(pathBase);
}

app.UseSerilogRequestLogging(options =>
{
    options.MessageTemplate = "Orbit {RequestMethod} {RequestPath} => {StatusCode} in {Elapsed:0.0000} ms";
    options.GetLevel = (httpContext, elapsed, exception) =>
        exception is not null || httpContext.Response.StatusCode >= 500
            ? LogEventLevel.Error
            : LogEventLevel.Information;
});

app.UseDefaultFiles();
app.UseStaticFiles();

// Map API endpoints for health, stats, sources, query, index, delete, and purge operations.
app.MapGet("/api/health", async (CancellationToken cancellationToken) =>
{
    var health = await managementService.HealthCheckAsync(cancellationToken);
    return Results.Json(new { health.IsHealthy, health.Message });
});

app.MapGet("/api/stats", async (CancellationToken cancellationToken) =>
{
    var stats = await managementService.GetStatsAsync(cancellationToken);
    return Results.Json(stats);
});

app.MapGet("/api/sources", async (int? limit, CancellationToken cancellationToken) =>
{
    var sources = await managementService.ListSourcesAsync(cancellationToken);
    var selected = sources.Take(Math.Clamp(limit ?? 100, 1, 500)).ToList();
    return Results.Json(selected);
});

app.MapPost("/api/query", async (QueryRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.Text))
    {
        return Results.BadRequest(new { error = "text is required" });
    }

    var topK = Math.Clamp(request.Limit ?? settings.Retrieval.DefaultTopK, 1, 20);
    var results = await queryService.QueryAsync(request.Text, topK, cancellationToken);
    return Results.Json(new QueryResponse(request.Text, topK, results));
});

app.MapPost("/api/index", async (IndexRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.SourcePath))
    {
        return Results.BadRequest(new { error = "sourcePath is required" });
    }

    var summary = await indexer.IndexDirectoryAsync(request.SourcePath, cancellationToken);
    return Results.Json(summary);
});

app.MapPost("/api/source/delete", async (DeleteRequest request, CancellationToken cancellationToken) =>
{
    if (string.IsNullOrWhiteSpace(request.SourcePath))
    {
        return Results.BadRequest(new { error = "sourcePath is required" });
    }

    var deleted = await managementService.DeleteSourceAsync(request.SourcePath, cancellationToken);
    return Results.Json(new { deleted });
});

app.MapPost("/api/purge", async (PurgeRequest request, CancellationToken cancellationToken) =>
{
    if (!string.Equals(request.ConfirmPhrase, "PURGE ALL", StringComparison.Ordinal))
    {
        return Results.BadRequest(new { error = "confirmPhrase must be PURGE ALL" });
    }

    await managementService.PurgeAllAsync(cancellationToken);
    return Results.Json(new { purged = true });
});

app.MapPost("/api/client-errors", (ClientErrorRequest request) =>
{
    if (string.IsNullOrWhiteSpace(request.Message))
    {
        return Results.BadRequest(new { error = "message is required" });
    }

    var severity = string.IsNullOrWhiteSpace(request.Severity) ? "error" : request.Severity;
    var message = TruncateForLogs(request.Message, 400);
    var source = TruncateForLogs(request.Source, 200);
    var url = TruncateForLogs(request.Url, 400);
    var stack = TruncateForLogs(request.Stack, 1500);

    Log.Warning("Client-side nebula flare [{Severity}] {Message} | source={Source} | url={Url} | ts={ClientTimestamp}", severity, message, source, url, request.Timestamp);
    if (!string.IsNullOrWhiteSpace(stack))
    {
        Log.Warning("Client stack trace: {ClientStack}", stack);
    }

    return Results.Accepted();
});

// Handle MCP JSON-RPC transport for initialize, ping, tool listing, and tool execution.
app.MapPost("/mcp", async (JsonObject request, CancellationToken cancellationToken) =>
{
    var method = request["method"]?.GetValue<string>();
    var id = request["id"]?.DeepClone();
    var parameters = request["params"]?.AsObject();

    if (string.IsNullOrWhiteSpace(method))
    {
        return Results.Json(BuildError(id, -32600, "Invalid request"));
    }

    if (method == "initialize")
    {
        return Results.Json(BuildResult(id, new JsonObject
        {
            ["protocolVersion"] = "2025-11-25",
            ["serverInfo"] = new JsonObject { ["name"] = "Nebula RAG", ["version"] = "0.2.0" },
            ["capabilities"] = new JsonObject { ["tools"] = new JsonObject { ["listChanged"] = false } }
        }));
    }

    if (method == "ping")
    {
        return Results.Json(BuildResult(id, new JsonObject()));
    }

    if (method == "tools/list")
    {
        return Results.Json(BuildResult(id, BuildToolsList()));
    }

    if (method != "tools/call")
    {
        return Results.Json(BuildError(id, -32601, $"Method not found: {method}"));
    }

    var toolName = parameters?["name"]?.GetValue<string>();
    var arguments = parameters?["arguments"]?.AsObject();
    if (string.IsNullOrWhiteSpace(toolName))
    {
        return Results.Json(BuildError(id, -32602, "Missing tool name"));
    }

    var result = await ExecuteToolAsync(toolName, arguments, queryService, managementService, sourcesManifestService, store, chunker, embeddingGenerator, indexer, settings, httpClient, cancellationToken);
    return Results.Json(BuildResult(id, result));
});

Log.Information(
    "NebulaRAG flight deck online. Dashboard: {DashboardPath} | MCP: {McpPath}",
    PrefixPath(pathBase, "/dashboard/"),
    PrefixPath(pathBase, "/mcp"));

app.Run();

/// <summary>
/// Prefixes an application route with the configured path base.
/// </summary>
/// <param name="pathBase">Configured ASP.NET path base.</param>
/// <param name="route">Route path starting with '/'.</param>
/// <returns>Combined route including path base when configured.</returns>
static string PrefixPath(string pathBase, string route)
{
    if (string.IsNullOrWhiteSpace(pathBase))
    {
        return route;
    }

    return $"{pathBase}{route}";
}

/// <summary>
/// Truncates a string for compact human-readable logs.
/// </summary>
/// <param name="value">Input value to truncate.</param>
/// <param name="maxLength">Maximum number of characters to keep.</param>
/// <returns>Original text when short enough; otherwise truncated text with ellipsis.</returns>
static string TruncateForLogs(string? value, int maxLength)
{
    if (string.IsNullOrWhiteSpace(value))
    {
        return string.Empty;
    }

    if (value.Length <= maxLength)
    {
        return value;
    }

    return $"{value[..maxLength]}...";
}

/// <summary>
/// Normalizes a path base value from environment configuration.
/// Ensures the value starts with '/' and does not end with '/'.
/// Returns empty string when no path base is configured.
/// </summary>
/// <param name="rawPathBase">Raw path base value (for example '/nebula' or 'nebula').</param>
/// <returns>Normalized path base suitable for ASP.NET Core UsePathBase.</returns>
static string NormalizePathBase(string? rawPathBase)
{
    if (string.IsNullOrWhiteSpace(rawPathBase))
    {
        return string.Empty;
    }

    var trimmed = rawPathBase.Trim();
    if (trimmed == "/")
    {
        return string.Empty;
    }

    if (!trimmed.StartsWith("/", StringComparison.Ordinal))
    {
        trimmed = $"/{trimmed}";
    }

    return trimmed.TrimEnd('/');
}

/// <summary>
/// Loads runtime settings from container mount path and environment variables.
/// Prioritizes /app/ragsettings.json if it exists, falls back to environment configuration.
/// </summary>
/// <returns>Configured RagSettings instance with database and retrieval defaults.</returns>
static RagSettings LoadSettings()
{
    var configBuilder = new ConfigurationBuilder();
    // Load from container mount path first
    configBuilder.AddJsonFile("/app/ragsettings.json", optional: true, reloadOnChange: false);
    // Environment variables override config file settings
    configBuilder.AddEnvironmentVariables(prefix: "NEBULARAG_");
    return configBuilder.Build().Get<RagSettings>() ?? new RagSettings();
}

/// <summary>
/// Builds the MCP tool catalog JSON object for tools/list responses.
/// Registers all supported RAG operations (query, index, health, stats, management).
/// </summary>
/// <returns>JSON object containing tools array with all RAG tool definitions.</returns>
static JsonObject BuildToolsList()
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
/// Builds a single MCP tool definition with minimal input schema.
/// Used for tools/list responses to advertise available operations to MCP clients.
/// </summary>
/// <param name="name">The tool identifier (e.g., 'query_project_rag').</param>
/// <param name="description">Human-readable description of the tool's purpose.</param>
/// <returns>JSON object conforming to MCP tool schema.</returns>
static JsonObject BuildToolDefinition(string name, string description)
{
    return new JsonObject
    {
        ["name"] = name,
        ["description"] = description,
        // Empty properties indicate tool accepts no input schema (or accepts arbitrary arguments)
        ["inputSchema"] = new JsonObject
        {
            ["type"] = "object",
            ["properties"] = new JsonObject()
        }
    };
}

/// <summary>
/// Executes a named MCP tool and returns the result payload.
/// Dispatches to specific tool handlers (query, index, health, management operations).
/// Wraps execution in try-catch to return error details via MCP result format.
/// </summary>
/// <param name="toolName">The name of the tool to execute (e.g., 'query_project_rag').</param>
/// <param name="arguments">JSON object containing tool-specific arguments.</param>
/// <param name="queryService">RAG query service for semantic search operations.</param>
/// <param name="managementService">RAG management service for stats, health, purge operations.</param>
/// <param name="store">PostgreSQL store for schema initialization and direct access.</param>
/// <param name="indexer">RAG indexer for document processing and embedding.</param>
/// <param name="settings">Runtime configuration (vector dims, retrieval defaults, etc.).</param>
/// <param name="cancellationToken">Cancellation token for async operations.</param>
/// <returns>MCP tool result payload with content and optional structured data.</returns>
static async Task<JsonObject> ExecuteToolAsync(
    string toolName,
    JsonObject? arguments,
    RagQueryService queryService,
    RagManagementService managementService,
    RagSourcesManifestService sourcesManifestService,
    PostgresRagStore store,
    TextChunker chunker,
    IEmbeddingGenerator embeddingGenerator,
    RagIndexer indexer,
    RagSettings settings,
    HttpClient httpClient,
    CancellationToken cancellationToken)
{
    try
    {
        // Dispatch to specific tool handlers by name
        if (toolName == RagInitSchemaToolName)
        {
            await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions, cancellationToken);
            var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, null, cancellationToken);
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

            var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? settings.Retrieval.DefaultTopK, 1, 20);
            var results = await queryService.QueryAsync(text, limit, cancellationToken);
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
            var health = await managementService.HealthCheckAsync(cancellationToken);
            return BuildToolResult(health.Message, new JsonObject { ["isHealthy"] = health.IsHealthy }, isError: !health.IsHealthy);
        }

        if (toolName == RagServerInfoToolName)
        {
            return BuildToolResult("Server info.", new JsonObject
            {
                ["serverName"] = "Nebula RAG",
                ["databaseHost"] = settings.Database.Host,
                ["databasePort"] = settings.Database.Port,
                ["databaseName"] = settings.Database.Database
            });
        }

        if (toolName == RagIndexStatsToolName)
        {
            var stats = await managementService.GetStatsAsync(cancellationToken);
            return BuildToolResult("Index stats.", new JsonObject
            {
                ["documentCount"] = stats.DocumentCount,
                ["chunkCount"] = stats.ChunkCount,
                ["totalTokens"] = stats.TotalTokens
            });
        }

        if (toolName == RagListSourcesToolName)
        {
            var sources = await managementService.ListSourcesAsync(cancellationToken);
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

            var summary = await indexer.IndexDirectoryAsync(sourcePath, cancellationToken);
            var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, sourcePath, cancellationToken);
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

            var chunks = chunker.Chunk(content, settings.Ingestion.ChunkSize, settings.Ingestion.ChunkOverlap);
            if (chunks.Count == 0)
            {
                return BuildToolResult("No indexable chunks produced.", isError: true);
            }

            var chunkEmbeddings = chunks
                .Select(chunk => new ChunkEmbedding(
                    chunk.Index,
                    chunk.Text,
                    chunk.TokenCount,
                    embeddingGenerator.GenerateEmbedding(chunk.Text, settings.Ingestion.VectorDimensions)))
                .ToList();

            var contentHash = Convert.ToHexString(System.Security.Cryptography.SHA256.HashData(Encoding.UTF8.GetBytes(content)));
            var updated = await store.UpsertDocumentAsync(sourcePath, contentHash, chunkEmbeddings, cancellationToken);
            var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, sourcePath, cancellationToken);

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

            var fetchedContent = await httpClient.GetStringAsync(url, cancellationToken);
            var targetSourcePath = string.IsNullOrWhiteSpace(sourcePath) ? url : sourcePath;
            var indexArgs = new JsonObject
            {
                ["sourcePath"] = targetSourcePath,
                ["content"] = fetchedContent
            };

            var indexResult = await ExecuteToolAsync(RagIndexTextToolName, indexArgs, queryService, managementService, sourcesManifestService, store, chunker, embeddingGenerator, indexer, settings, httpClient, cancellationToken);
            return indexResult;
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
            var reindexArgs = new JsonObject
            {
                ["sourcePath"] = sourcePath,
                ["content"] = content
            };

            return await ExecuteToolAsync(RagIndexTextToolName, reindexArgs, queryService, managementService, sourcesManifestService, store, chunker, embeddingGenerator, indexer, settings, httpClient, cancellationToken);
        }

        if (toolName == RagGetChunkToolName)
        {
            var chunkId = arguments?["chunkId"]?.GetValue<long?>();
            if (chunkId is null || chunkId.Value <= 0)
            {
                return BuildToolResult("chunkId is required and must be > 0.", isError: true);
            }

            var chunk = await store.GetChunkByIdAsync(chunkId.Value, cancellationToken);
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

            var limit = Math.Clamp(arguments?["limit"]?.GetValue<int?>() ?? settings.Retrieval.DefaultTopK, 1, 20);
            var results = await queryService.QueryAsync(text, limit, cancellationToken);
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

            var deleted = await managementService.DeleteSourceAsync(sourcePath, cancellationToken);
            var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, sourcePath, cancellationToken);
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

            await managementService.PurgeAllAsync(cancellationToken);
            var manifestSyncResult = await TrySyncRagSourcesManifestAsync(sourcesManifestService, null, cancellationToken);
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
            var embedding = embeddingGenerator.GenerateEmbedding(content, settings.Ingestion.VectorDimensions);
            var memoryId = await store.CreateMemoryAsync(sessionId, type, content, tags, embedding, cancellationToken);

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
            var queryEmbedding = embeddingGenerator.GenerateEmbedding(text, settings.Ingestion.VectorDimensions);
            var memories = await store.SearchMemoriesAsync(queryEmbedding, limit, type, tag, cancellationToken);
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
            var memories = await store.ListMemoriesAsync(limit, type, tag, cancellationToken);
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

            var deleted = await store.DeleteMemoryAsync(memoryId.Value, cancellationToken);
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
            var embedding = string.IsNullOrWhiteSpace(content)
                ? null
                : embeddingGenerator.GenerateEmbedding(content, settings.Ingestion.VectorDimensions);

            var updated = await store.UpdateMemoryAsync(memoryId.Value, type, content, tags, embedding, cancellationToken);
            return BuildToolResult(updated ? "Memory updated." : "Memory not found.", new JsonObject { ["updated"] = updated });
        }

        return BuildToolResult($"Unknown tool: {toolName}", isError: true);
    }
    catch (Exception ex)
    {
        // Catch all exceptions and return error via MCP result format
        return BuildToolResult($"Tool execution failed: {ex.Message}", isError: true);
    }
}

/// <summary>
/// Returns a JSON-RPC 2.0 success envelope wrapping the result.
/// </summary>
/// <param name="id">The request ID to echo back in the response.</param>
/// <param name="result">The result payload (typically a JsonObject).</param>
/// <returns>JSON-RPC response with jsonrpc, id, and result fields.</returns>
static JsonObject BuildResult(JsonNode? id, JsonObject result)
{
    return new JsonObject
    {
        ["jsonrpc"] = "2.0",
        ["id"] = id,
        ["result"] = result
    };
}

/// <summary>
/// Returns a JSON-RPC 2.0 error envelope with error code and message.
/// </summary>
/// <param name="id">The request ID to echo back in the error response.</param>
/// <param name="code">JSON-RPC error code (e.g., -32600 for Invalid Request).</param>
/// <param name="message">Human-readable error description.</param>
/// <returns>JSON-RPC error response with jsonrpc, id, and error fields.</returns>
static JsonObject BuildError(JsonNode? id, int code, string message)
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
/// Creates an MCP tool result payload with text and optional structured content.
/// Wraps content in the standard MCP content array format.
/// </summary>
/// <param name="text">The human-readable result message or error description.</param>
/// <param name="structuredContent">Optional JSON object with structured tool output (e.g., query matches, stats).</param>
/// <param name="isError">If true, marks the result as an error (sets isError=true in response).</param>
/// <returns>JSON object conforming to MCP tool result schema.</returns>
static JsonObject BuildToolResult(string text, JsonObject? structuredContent = null, bool isError = false)
{
    var result = new JsonObject
    {
        // MCP protocol requires content as an array of typed content blocks
        ["content"] = new JsonArray
        {
            new JsonObject
            {
                ["type"] = "text",
                ["text"] = text
            }
        }
    };

    // Add structured data if provided (for complex query results, stats, etc.)
    if (structuredContent is not null)
    {
        result["structuredContent"] = structuredContent;
    }

    // Mark as error to signal failure to MCP client
    if (isError)
    {
        result["isError"] = true;
    }

    return result;
}

/// <summary>
/// Synchronizes rag-sources markdown while keeping tool execution resilient to write failures.
/// </summary>
/// <param name="sourcesManifestService">Manifest synchronization service.</param>
/// <param name="contextPath">Optional path context used for manifest destination resolution.</param>
/// <param name="cancellationToken">Cancellation token.</param>
/// <returns>Sync result when successful; otherwise <c>null</c>.</returns>
static async Task<RagSourcesManifestSyncResult?> TrySyncRagSourcesManifestAsync(RagSourcesManifestService sourcesManifestService, string? contextPath, CancellationToken cancellationToken)
{
    try
    {
        return await sourcesManifestService.SyncAsync(contextPath, cancellationToken);
    }
    catch (Exception ex)
    {
        Log.Warning(ex, "Failed to synchronize rag-sources manifest after successful index operation.");
        return null;
    }
}

/// <summary>
/// Parses tags from MCP arguments into a normalized string list.
/// </summary>
/// <param name="tagsNode">JSON node expected to contain an array of strings.</param>
/// <returns>Tag list, empty when node is null or invalid.</returns>
static List<string> ParseTags(JsonNode? tagsNode)
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
/// Trims chunk text to a compact snippet for MCP tool output.
/// Flattens newlines and limits length to improve readability in compact output.
/// </summary>
/// <param name="source">The original chunk text (may contain newlines and excess whitespace).</param>
/// <returns>Flattened string, truncated to 280 characters with ellipsis if needed.</returns>
static string TrimSnippet(string source)
{
    // Flatten newlines and carriage returns into spaces for single-line display
    var flattened = source.Replace('\r', ' ').Replace('\n', ' ').Trim();
    // Truncate at 280 chars for compact MCP output
    return flattened.Length <= 280 ? flattened : $"{flattened[..280]}...";
}

/// <summary>
/// Query API request model.
/// </summary>
/// <param name="Text">The semantic search query text.</param>
/// <param name="Limit">Optional maximum number of results (1-20, defaults to configuration).</param>
internal sealed record QueryRequest(string Text, int? Limit);

/// <summary>
/// Query API response model.
/// </summary>
/// <param name="Query">The original query text from the request.</param>
/// <param name="Limit">The effective result limit applied.</param>
/// <param name="Matches">List of matching chunks ranked by relevance.</param>
internal sealed record QueryResponse(string Query, int Limit, IReadOnlyList<RagSearchResult> Matches);

/// <summary>
/// Index API request model.
/// </summary>
/// <param name="SourcePath">Directory path to recursively index for documents.</param>
internal sealed record IndexRequest(string SourcePath);

/// <summary>
/// Delete source API request model.
/// </summary>
/// <param name="SourcePath">The indexed source path to delete.</param>
internal sealed record DeleteRequest(string SourcePath);

/// <summary>
/// Purge all request model.
/// </summary>
/// <param name="ConfirmPhrase">Must be exactly "PURGE ALL" to execute (safety gate).</param>
internal sealed record PurgeRequest(string ConfirmPhrase);

/// <summary>
/// Browser error report payload sent by the dashboard.
/// </summary>
/// <param name="Message">Error message captured in the browser runtime.</param>
/// <param name="Stack">Optional JavaScript stack trace.</param>
/// <param name="Source">Source event type (window.error or unhandledrejection).</param>
/// <param name="Url">Current browser URL where the error occurred.</param>
/// <param name="UserAgent">Browser user agent string.</param>
/// <param name="Severity">Client-reported severity classification.</param>
/// <param name="Timestamp">Client timestamp (ISO-8601) for the event.</param>
internal sealed record ClientErrorRequest(string Message, string? Stack, string? Source, string? Url, string? UserAgent, string? Severity, string? Timestamp);
