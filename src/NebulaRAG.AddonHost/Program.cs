using System.Text;
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
using Serilog.Formatting.Compact;

const string QueryProjectRagToolName = "query_project_rag";
const string RagInitSchemaToolName = "rag_init_schema";
const string RagHealthCheckToolName = "rag_health_check";
const string RagServerInfoToolName = "rag_server_info";
const string RagIndexStatsToolName = "rag_index_stats";
const string RagListSourcesToolName = "rag_list_sources";
const string RagIndexPathToolName = "rag_index_path";
const string RagDeleteSourceToolName = "rag_delete_source";
const string RagPurgeAllToolName = "rag_purge_all";

Log.Logger = new LoggerConfiguration()
    .MinimumLevel.Information()
    .WriteTo.Console(new CompactJsonFormatter())
    .CreateLogger();

var builder = WebApplication.CreateBuilder(args);
builder.Host.UseSerilog();

builder.Services.Configure<JsonOptions>(options =>
{
    options.SerializerOptions.WriteIndented = true;
});

var settings = LoadSettings();
settings.Validate();

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
var indexer = new RagIndexer(store, chunker, embeddingGenerator, settings, loggerFactory.CreateLogger<RagIndexer>());

await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions);

var app = builder.Build();
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
            ["serverInfo"] = new JsonObject { ["name"] = "Nebula RAG Add-on MCP", ["version"] = "0.2.0" },
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

    var result = await ExecuteToolAsync(toolName, arguments, queryService, managementService, store, indexer, settings, cancellationToken);
    return Results.Json(BuildResult(id, result));
});

app.Run();

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
            BuildToolDefinition(RagDeleteSourceToolName, "Delete one indexed source path."),
            BuildToolDefinition(RagPurgeAllToolName, "Purge all indexed content.")
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
    PostgresRagStore store,
    RagIndexer indexer,
    RagSettings settings,
    CancellationToken cancellationToken)
{
    try
    {
        // Dispatch to specific tool handlers by name
        if (toolName == RagInitSchemaToolName)
        {
            await store.InitializeSchemaAsync(settings.Ingestion.VectorDimensions, cancellationToken);
            return BuildToolResult("Schema initialized.");
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
                ["serverName"] = "Nebula RAG Add-on MCP",
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
            return BuildToolResult("Index complete.", new JsonObject
            {
                ["documentsIndexed"] = summary.DocumentsIndexed,
                ["documentsSkipped"] = summary.DocumentsSkipped,
                ["chunksIndexed"] = summary.ChunksIndexed
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
            return BuildToolResult($"Deleted {deleted} items.", new JsonObject { ["deleted"] = deleted });
        }

        if (toolName == RagPurgeAllToolName)
        {
            var phrase = arguments?["confirmPhrase"]?.GetValue<string>();
            if (!string.Equals(phrase, "PURGE ALL", StringComparison.Ordinal))
            {
                return BuildToolResult("confirmPhrase must be PURGE ALL", isError: true);
            }

            await managementService.PurgeAllAsync(cancellationToken);
            return BuildToolResult("Purge complete.");
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
